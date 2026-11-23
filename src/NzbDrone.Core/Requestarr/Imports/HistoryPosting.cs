using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Commands;
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

        // "Post history now": every imported request not queued yet, for an import run before posting existed.
        int QueueEarlier();

        void Pause();
        void Resume();
        void Cancel(Actor actor);
    }

    // Posts imported history in Discord: each member's in their own channel, a few titles to a message, and every
    // request in the admin feed as its normal card marked Imported, oldest first, between a header and a closing post.
    // Everything is silent and pings nobody. It goes slowly, one post at a time with a pause between, so live notices
    // never queue behind it, and each post is recorded (HistoryPosts) before the next, so nothing is posted twice.
    public class HistoryPosting : IHistoryPosting, IExecute<PostHistoryCommand>, IHandle<MemberOnboardedEvent>, IHandle<ApplicationStartedEvent>
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
        private readonly IManageCommandQueue _commands;
        private readonly IAuditService _audit;
        private readonly Logger _logger;
        private readonly object _lock = new();
        private HistoryPostingState _state = HistoryPostingState.Idle;
        private bool _cancelled;

        // The feed's header went out for this batch: a resume after Pause doesn't post it again.
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
                              IManageCommandQueue commands,
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
            _commands = commands;
            _audit = audit;
            _logger = logger;
        }

        // Waits between posts; tests make it instant.
        public Func<TimeSpan, Task> Wait { get; set; } = Task.Delay;

        public HistoryPostingStatus Status()
        {
            lock (_lock)
            {
                return new HistoryPostingStatus(_state, _total, _posted);
            }
        }

        public int QueueImport(int afterRequestId) => Queue(_requests.All().Where(r => r.Imported && r.Id > afterRequestId).ToList());

        public int QueueEarlier() => Queue(_requests.All().Where(r => r.Imported).ToList());

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

        public void Cancel(Actor actor)
        {
            var waiting = _posts.Waiting();
            _posts.DeleteMany(waiting);

            lock (_lock)
            {
                _cancelled = _state == HistoryPostingState.Posting;

                if (!_cancelled)
                {
                    _state = HistoryPostingState.Idle;
                    _total = _posted;
                }
            }

            _audit.Record(actor, PostedAction, "import", null, $"cancelled with {waiting.Count} posts left");
        }

        public void Handle(MemberOnboardedEvent message)
        {
            if (_config.ImportPostChannels)
            {
                Queue(_requests.All().Where(r => r.Imported && r.OwnerUserId == message.UserId).ToList(), feed: false);
            }
        }

        // What waited when Requestarr stopped carries on.
        public void Handle(ApplicationStartedEvent message)
        {
            var waiting = _posts.Waiting().Count;

            if (waiting > 0)
            {
                lock (_lock)
                {
                    _total = waiting;
                    _posted = 0;
                }

                Start();
            }
        }

        public void Execute(PostHistoryCommand message) => PostAllAsync().GetAwaiter().GetResult();

        // Queues each request once per destination: the feed when it's switched on, the requester's channel when that's
        // switched on and they're onboarded. How many posts were queued.
        private int Queue(IReadOnlyCollection<Request> imported, bool feed = true)
        {
            var now = DateTime.UtcNow;
            var rows = new List<HistoryPost>();

            if (feed && _config.ImportPostFeed)
            {
                var known = _posts.Known(HistoryDestination.Feed);
                rows.AddRange(imported.Where(r => !known.Contains(r.Id)).Select(r => Row(r, HistoryDestination.Feed, now)));
            }

            if (_config.ImportPostChannels)
            {
                var known = _posts.Known(HistoryDestination.Channel);
                var onboarded = _users.All().Where(u => u.PrivateChannelId != null && u.Status == UserStatus.Active).Select(u => u.Id).ToHashSet();
                rows.AddRange(imported.Where(r => !known.Contains(r.Id) && onboarded.Contains(r.OwnerUserId)).Select(r => Row(r, HistoryDestination.Channel, now)));
            }

            if (rows.Count == 0)
            {
                return 0;
            }

            _posts.InsertMany(rows);

            lock (_lock)
            {
                // A new batch once the last one is done; otherwise these join the one under way.
                if (_state == HistoryPostingState.Idle)
                {
                    _total = 0;
                    _posted = 0;
                    _feedStarted = false;
                    _feedPosted = 0;
                }

                _total += rows.Count;
            }

            Start();
            return rows.Count;
        }

        private static HistoryPost Row(Request request, HistoryDestination destination, DateTime now) =>
            new() { RequestId = request.Id, Destination = destination, Queued = now };

        private void Start()
        {
            lock (_lock)
            {
                if (_state != HistoryPostingState.Idle)
                {
                    return;
                }

                _state = HistoryPostingState.Posting;
                _cancelled = false;
            }

            _commands.Push(new PostHistoryCommand());
        }

        private async Task PostAllAsync()
        {
            var waiting = _posts.Waiting();
            var requests = _requests.Get(waiting.Select(p => p.RequestId).Distinct()).ToDictionary(r => r.Id);

            foreach (var member in waiting.Where(p => p.Destination == HistoryDestination.Channel && requests.ContainsKey(p.RequestId)).GroupBy(p => requests[p.RequestId].OwnerUserId))
            {
                if (!await PostChannelAsync(member.Key, member.ToList(), requests))
                {
                    Finish(null);
                    return;
                }
            }

            var feed = waiting.Where(p => p.Destination == HistoryDestination.Feed && requests.ContainsKey(p.RequestId))
                .OrderBy(p => requests[p.RequestId].Requested).ThenBy(p => p.RequestId).ToList();
            Finish(await PostFeedAsync(feed, requests));
        }

        // A member's history, oldest first, a few titles to each silent message in their channel.
        private async Task<bool> PostChannelAsync(int userId, List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            if (!ulong.TryParse(_users.Find(userId)?.PrivateChannelId, out var channel))
            {
                return true;
            }

            var ordered = posts.OrderBy(p => requests[p.RequestId].Requested).ThenBy(p => p.RequestId).ToList();

            for (var start = 0; start < ordered.Count; start += LinesPerMessage)
            {
                var batch = ordered.Skip(start).Take(LinesPerMessage).ToList();
                var lines = batch.Select(p => DiscordText.HistoryLine(requests[p.RequestId])).Prepend(DiscordText.HistoryChannelHeader);

                if (!await PostAsync(batch, () => _gateway.SendMessageAsync(channel, new InteractionReply { Content = string.Join("\n", lines), Quiet = true })))
                {
                    return false;
                }
            }

            return true;
        }

        // The feed: a header, every request's own card marked Imported, oldest first, then a closing post. Whether it got
        // to the end; posted counts what went out.
        private async Task<(bool Finished, int Posted, int Total)?> PostFeedAsync(List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            if (posts.Count == 0 || !ulong.TryParse(_configs.FindByGuild(_gateway.GuildId.ToString())?.FeedChannelId, out var channel))
            {
                return null;
            }

            if (!_feedStarted)
            {
                var members = posts.Select(p => requests[p.RequestId].OwnerUserId).Distinct().Count();
                await _gateway.SendMessageAsync(channel, new InteractionReply { Content = DiscordText.HistoryFeedHeader(posts.Count, members), Quiet = true });
                _feedStarted = true;
            }

            var posted = 0;

            foreach (var post in posts)
            {
                if (!await PostAsync(new[] { post }, () => _feed.PostImportedAsync(requests[post.RequestId])))
                {
                    return (false, posted, posts.Count);
                }

                posted++;
                _feedPosted++;
            }

            return (true, posted, posts.Count);
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
            var now = DateTime.UtcNow;

            foreach (var row in rows)
            {
                row.Posted = now;
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

        // The feed's closing post (or where it stopped), and the state the Import page shows next.
        private void Finish((bool Finished, int Posted, int Total)? feed)
        {
            bool cancelled;
            int posted;

            lock (_lock)
            {
                cancelled = _cancelled;
                posted = _posted;

                if (_state == HistoryPostingState.Posting || cancelled)
                {
                    _state = HistoryPostingState.Idle;
                }
            }

            if (feed is { } f && (f.Finished || cancelled))
            {
                // Counted over the batch, across any pause: this run may have started part way.
                var left = f.Total - f.Posted;
                PostFeedNote(f.Finished ? DiscordText.HistoryFeedDone(_feedPosted) : DiscordText.HistoryFeedStopped(_feedPosted, _feedPosted + left));
                _feedStarted = false;
            }

            _audit.Record(Actor.System, PostedAction, "import", null, $"{posted} history posts sent{(cancelled ? ", then cancelled" : string.Empty)}");
            _logger.Info("Imported history: {0} posts sent", posted);
        }

        private void PostFeedNote(string text)
        {
            if (ulong.TryParse(_configs.FindByGuild(_gateway.GuildId.ToString())?.FeedChannelId, out var channel))
            {
                _gateway.SendMessageAsync(channel, new InteractionReply { Content = text, Quiet = true }).GetAwaiter().GetResult();
            }
        }
    }
}
