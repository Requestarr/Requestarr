using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Imports
{
    public enum HistoryPostingState
    {
        Idle,
        Posting,
        Paused
    }

    // What the Import page shows: "312 of 467 posted", and whether it's posting, paused or done.
    public record HistoryPostingStatus(HistoryPostingState State, int Total, int Posted);

    // A member was onboarded and has their channel now.
    public class MemberOnboardedEvent : IEvent
    {
        public MemberOnboardedEvent(int userId)
        {
            UserId = userId;
        }

        public int UserId { get; }
    }

    public interface IHistoryPosting
    {
        HistoryPostingStatus Status();

        // An import's new requests (IDs above afterRequestId): queued for the feed and their onboarded requesters' channels.
        int QueueImport(int afterRequestId);

        // "Post history now": every imported request not posted where it belongs yet (an import run before posting
        // existed, or a new server), and whatever was left waiting.
        int QueueEarlier();

        void Pause();
        void Resume();
        void Cancel(Actor actor);
    }

    // Posts imported history in Discord: each member's in their own channel, a few titles to a message, and every
    // request in the admin feed as its normal card marked Imported, oldest first, between a header and a closing post.
    // Everything is silent and pings nobody. One background worker posts slowly, a pause between posts, so live notices
    // never queue behind it; it keeps going while anything waits, so rows queued mid-run are posted too. (Servarr's
    // command queue drops a second push of a command already running, so the import's own command pattern can't be
    // used here.) Each post is recorded per request, destination and channel before the next: nothing is posted twice.
    public class HistoryPosting : IHistoryPosting, IHandle<MemberOnboardedEvent>, IHandle<ApplicationStartedEvent>
    {
        public const string PostedAction = "import.history";

        // Discord allows 5 messages every 5 seconds a channel; one every 2 seconds leaves live notices plenty of room.
        public static readonly TimeSpan Pace = TimeSpan.FromSeconds(2);

        // Lines a member's history message holds at most, well inside Discord's 2,000 characters.
        private const int LinesPerMessage = 15;

        private readonly IHistoryPostRepository _posts;
        private readonly IRequestRepository _requests;
        private readonly IRequestarrUserRepository _users;
        private readonly IRequestFeed _feed;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IConfigService _config;
        private readonly IAuditService _audit;
        private readonly Logger _logger;
        private readonly object _lock = new();
        private HistoryPostingState _state = HistoryPostingState.Idle;
        private bool _running;
        private bool _again;
        private bool _cancelled;

        // This batch's feed: its header went out (a resume or restart doesn't post it again), and how many cards since.
        private bool _feedStarted;
        private int _feedPosted;
        private int _total;
        private int _posted;

        public HistoryPosting(IHistoryPostRepository posts,
                              IRequestRepository requests,
                              IRequestarrUserRepository users,
                              IRequestFeed feed,
                              IDiscordGuildGateway gateway,
                              IDiscordGuildConfigRepository configs,
                              IConfigService config,
                              IAuditService audit,
                              Logger logger)
        {
            _posts = posts;
            _requests = requests;
            _users = users;
            _feed = feed;
            _gateway = gateway;
            _configs = configs;
            _config = config;
            _audit = audit;
            _logger = logger;
        }

        // Waits between posts, and runs the worker in the background; tests make both immediate.
        public Func<TimeSpan, Task> Wait { get; set; } = Task.Delay;
        public Func<Func<Task>, Task> Background { get; set; } = work => Task.Run(work);

        public HistoryPostingStatus Status()
        {
            lock (_lock)
            {
                return new HistoryPostingStatus(_state, _total, _posted);
            }
        }

        public int QueueImport(int afterRequestId) => Queue(_requests.All().Where(r => r.Imported && r.Id > afterRequestId).ToList(), true);

        public int QueueEarlier() => Queue(_requests.All().Where(r => r.Imported).ToList(), true);

        public void Pause()
        {
            lock (_lock)
            {
                if (_state == HistoryPostingState.Posting)
                {
                    _state = HistoryPostingState.Paused;
                }
            }
        }

        public void Resume()
        {
            lock (_lock)
            {
                if (_state != HistoryPostingState.Paused)
                {
                    return;
                }

                _state = HistoryPostingState.Idle;
            }

            Start();
        }

        // While the worker runs it stops at its next post and drops what waits; otherwise that's done here.
        public void Cancel(Actor actor)
        {
            lock (_lock)
            {
                if (_running)
                {
                    _cancelled = true;
                    _state = HistoryPostingState.Posting;
                }
            }

            if (!_cancelled)
            {
                DropWaiting();
            }

            _audit.Record(actor, PostedAction, "import", null, "cancelled");
        }

        public void Handle(MemberOnboardedEvent message)
        {
            if (_config.ImportPostChannels)
            {
                Queue(_requests.All().Where(r => r.Imported && r.OwnerUserId == message.UserId).ToList(), false);
            }
        }

        // What waited when Requestarr stopped carries on, its feed header not posted again.
        public void Handle(ApplicationStartedEvent message)
        {
            var waiting = _posts.Waiting();

            if (waiting.Count == 0)
            {
                return;
            }

            var batches = waiting.Select(p => p.Queued).ToHashSet();
            var feedDone = _posts.All().Count(p => p.Destination == HistoryDestination.Feed && p.Posted != null && batches.Contains(p.Queued));

            lock (_lock)
            {
                _total = waiting.Count;
                _feedStarted = feedDone > 0;
                _feedPosted = feedDone;
            }

            Start();
        }

        // Queues each request once per destination and channel: the feed's when it's switched on, the requester's
        // channel when that's switched on and they're onboarded. How many posts were queued; posting starts either way.
        private int Queue(IReadOnlyCollection<Request> imported, bool feed)
        {
            var now = DateTime.UtcNow;
            var rows = new List<HistoryPost>();

            if (feed && _config.ImportPostFeed && FeedChannel() is { } feedChannel)
            {
                var known = _posts.Known(HistoryDestination.Feed, feedChannel);
                rows.AddRange(imported.Where(r => !known.Contains(r.Id)).Select(r => Row(r, HistoryDestination.Feed, feedChannel, now)));
            }

            if (_config.ImportPostChannels)
            {
                rows.AddRange(ChannelRows(imported, now));
            }

            _posts.InsertMany(rows);
            Grow(rows.Count);
            Start();

            return rows.Count;
        }

        private IEnumerable<HistoryPost> ChannelRows(IEnumerable<Request> imported, DateTime now)
        {
            var channels = _users.All().Where(u => u.PrivateChannelId != null && u.Status == UserStatus.Active).ToDictionary(u => u.Id, u => u.PrivateChannelId);

            foreach (var owner in imported.Where(r => channels.ContainsKey(r.OwnerUserId)).GroupBy(r => r.OwnerUserId))
            {
                var known = _posts.Known(HistoryDestination.Channel, channels[owner.Key]);

                foreach (var request in owner.Where(r => !known.Contains(r.Id)))
                {
                    yield return Row(request, HistoryDestination.Channel, channels[owner.Key], now);
                }
            }
        }

        private static HistoryPost Row(Request request, HistoryDestination destination, string channel, DateTime now) =>
            new() { RequestId = request.Id, Destination = destination, ChannelId = channel, Queued = now };

        // A new batch once the last one is done (the page keeps the last one's count until then); otherwise these join
        // the one under way.
        private void Grow(int rows)
        {
            lock (_lock)
            {
                if (rows > 0 && _state == HistoryPostingState.Idle && !_running)
                {
                    _total = 0;
                    _posted = 0;
                    _feedStarted = false;
                    _feedPosted = 0;
                }

                _total += rows;
            }
        }

        // Wakes the worker, or starts it; a pause holds everything until Resume.
        private void Start()
        {
            lock (_lock)
            {
                if (_state == HistoryPostingState.Paused || !_posts.Waiting().Any())
                {
                    return;
                }

                _state = HistoryPostingState.Posting;

                if (_running)
                {
                    _again = true;
                    return;
                }

                _running = true;
                _again = false;
            }

            _ = Background(WorkAsync);
        }

        private async Task WorkAsync()
        {
            try
            {
                do
                {
                    await DrainAsync();
                }
                while (KeepGoing());
            }
            catch (Exception ex)
            {
                // Discord down, say: what waits stays queued, and Post history now (or a restart) carries on.
                _logger.Warn(ex, "Posting imported history stopped; what waits is posted on the next start");
                lock (_lock)
                {
                    _running = false;
                    _state = _state == HistoryPostingState.Paused ? _state : HistoryPostingState.Idle;
                }
            }
        }

        // Whether something was queued while the last pass ran; otherwise the worker stops, and the state says why.
        private bool KeepGoing()
        {
            lock (_lock)
            {
                if (_again && _state == HistoryPostingState.Posting && !_cancelled)
                {
                    _again = false;
                    return true;
                }

                _running = false;

                if (_state == HistoryPostingState.Posting)
                {
                    _state = HistoryPostingState.Idle;
                }

                return false;
            }
        }

        // Posts what waits, read afresh each pass so rows queued meanwhile go too; members' channels first, then the feed.
        private async Task DrainAsync()
        {
            while (Continuing() && _posts.Waiting() is { Count: > 0 } waiting)
            {
                var requests = _requests.Get(waiting.Select(p => p.RequestId).Distinct()).ToDictionary(r => r.Id);
                var members = waiting.Where(p => p.Destination == HistoryDestination.Channel)
                    .GroupBy(p => p.ChannelId).ToList();

                foreach (var member in members)
                {
                    await PostChannelAsync(member.Key, member.ToList(), requests);
                }

                var feed = waiting.Where(p => p.Destination == HistoryDestination.Feed)
                    .OrderBy(p => requests.GetValueOrDefault(p.RequestId)?.Requested).ThenBy(p => p.RequestId).ToList();
                await PostFeedAsync(feed, requests);
            }

            Wrap();
        }

        // A member's history, oldest first, a few titles to each silent message in their channel.
        private async Task PostChannelAsync(string channelId, List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            if (!ulong.TryParse(channelId, out var channel) || Unsendable(posts, requests))
            {
                return;
            }

            var ordered = posts.OrderBy(p => requests[p.RequestId].Requested).ThenBy(p => p.RequestId).ToList();

            for (var start = 0; start < ordered.Count && Continuing(); start += LinesPerMessage)
            {
                var batch = ordered.Skip(start).Take(LinesPerMessage).ToList();
                var lines = batch.Select(p => DiscordText.HistoryLine(requests[p.RequestId])).Prepend(DiscordText.HistoryChannelHeader);

                await PostAsync(batch, () => _gateway.SendMessageAsync(channel, new InteractionReply { Content = string.Join("\n", lines), Quiet = true }));
            }
        }

        // The feed: its header once a batch, then every request's own card marked Imported, oldest first.
        private async Task PostFeedAsync(List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            if (posts.Count == 0 || Unsendable(posts, requests) || FeedChannel() is not { } channelId)
            {
                return;
            }

            if (!_feedStarted)
            {
                var members = posts.Select(p => requests[p.RequestId].OwnerUserId).Distinct().Count();
                await Note(channelId, DiscordText.HistoryFeedHeader(posts.Count, members));
                _feedStarted = true;
            }

            foreach (var post in posts.TakeWhile(_ => Continuing()))
            {
                if (await PostAsync(new[] { post }, () => _feed.PostImportedAsync(requests[post.RequestId])))
                {
                    _feedPosted++;
                }
            }
        }

        // Rows that can't go out (their request was deleted, or no feed channel is set) are dropped, not left waiting.
        private bool Unsendable(List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            var gone = posts.Where(p => !requests.ContainsKey(p.RequestId) || (p.Destination == HistoryDestination.Feed && FeedChannel() == null)).ToList();

            if (gone.Count > 0)
            {
                _posts.DeleteMany(gone);
                posts.RemoveAll(gone.Contains);
                Grow(-gone.Count);
            }

            return posts.Count == 0;
        }

        // One post after the pace's wait, recorded on its rows before the next; false when paused or cancelled first.
        private async Task<bool> PostAsync(IReadOnlyCollection<HistoryPost> rows, Func<Task<ulong>> send)
        {
            await Wait(Pace);

            if (!Continuing())
            {
                return false;
            }

            var id = await send();

            foreach (var row in rows)
            {
                row.Posted = DateTime.UtcNow;
                row.MessageId = id == 0 ? null : id.ToString();
            }

            _posts.UpdateMany(rows.ToList());

            lock (_lock)
            {
                _posted += rows.Count;
            }

            return true;
        }

        private bool Continuing()
        {
            lock (_lock)
            {
                return _state == HistoryPostingState.Posting && !_cancelled;
            }
        }

        // After a pass: a cancel drops what waits; a finished feed gets its closing post. A pause leaves everything.
        private void Wrap()
        {
            bool cancelled;

            lock (_lock)
            {
                cancelled = _cancelled;
                _cancelled = false;
            }

            if (cancelled)
            {
                DropWaiting();
            }
            else if (Continuing() && _feedStarted && !_posts.Waiting().Any(p => p.Destination == HistoryDestination.Feed))
            {
                PostClosing(DiscordText.HistoryFeedDone(_feedPosted));
                _audit.Record(Actor.System, PostedAction, "import", null, $"{_posted} history posts sent");
            }
        }

        // Cancel: what waits goes, the feed says where it stopped, and the count ends at what was posted.
        private void DropWaiting()
        {
            var waiting = _posts.Waiting();
            _posts.DeleteMany(waiting);

            if (_feedStarted)
            {
                var left = waiting.Count(p => p.Destination == HistoryDestination.Feed);
                PostClosing(DiscordText.HistoryFeedStopped(_feedPosted, _feedPosted + left));
            }

            lock (_lock)
            {
                _state = HistoryPostingState.Idle;
                _total = _posted;
            }
        }

        private void PostClosing(string text)
        {
            if (FeedChannel() is { } channel)
            {
                Note(channel, text).GetAwaiter().GetResult();
            }

            _feedStarted = false;
        }

        private Task<ulong> Note(string channel, string text) =>
            _gateway.SendMessageAsync(ulong.Parse(channel), new InteractionReply { Content = text, Quiet = true });

        // The admin feed channel of the server the bot is in now; null when it has none.
        private string FeedChannel() =>
            _configs.FindByGuild(_gateway.GuildId.ToString())?.FeedChannelId is { } id && ulong.TryParse(id, out _) ? id : null;
    }
}
