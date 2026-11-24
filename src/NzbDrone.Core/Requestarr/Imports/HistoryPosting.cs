using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging;
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

    // What the server's window shows: "312 of 467 posted", and whether it's posting, paused or done.
    public record HistoryPostingStatus(HistoryPostingState State, int Total, int Posted);

    // Whether onboarding someone posts their imported requests in their new channel, leaves them for later, or does what
    // the server's setting says.
    public enum OnboardHistory
    {
        AsSet,
        Post,
        Leave
    }

    // A member was onboarded and has their channel now.
    public class MemberOnboardedEvent : IEvent
    {
        public MemberOnboardedEvent(int userId, OnboardHistory history)
        {
            UserId = userId;
            History = history;
        }

        public int UserId { get; }
        public OnboardHistory History { get; }
    }

    public interface IHistoryPosting
    {
        HistoryPostingStatus Status();

        // An import's new requests (IDs above afterRequestId): queued for the feed.
        void QueueImport(int afterRequestId);

        // One member's imported requests in their own channel, once: asked for by them (/history) or an admin.
        ServiceResult PostMember(int userId, Actor actor);

        // The whole server's: every imported request not posted where it belongs yet, in the feed and in its requester's
        // channel, and whatever was left waiting.
        ServiceResult PostServer(Actor actor);

        void Pause();
        void Resume();
        void Cancel(Actor actor);
    }

    // Posts imported history in Discord: each member's in their own channel and every request in the admin feed, each as
    // a card marked Imported, oldest first, the feed's between a header and a closing post.
    // Everything is silent and pings nobody. One background worker posts slowly, a pause between posts, so live notices
    // never queue behind it; it keeps going while anything waits, so rows queued mid-run are posted too. (Servarr's
    // command queue drops a second push of a command already running, so the import's own command pattern can't be
    // used here; PostImportedHistoryCommand only shows the worker under System, then Tasks.) Each post is recorded per
    // request, destination and channel before the next: nothing is posted twice.
    public class HistoryPosting : IHistoryPosting, IHandle<MemberOnboardedEvent>, IHandle<ApplicationStartedEvent>
    {
        public const string PostedAction = "import.history";

        // Discord allows 5 messages every 5 seconds a channel; one every 2 seconds leaves live notices plenty of room.
        public static readonly TimeSpan Pace = TimeSpan.FromSeconds(2);

        private readonly IHistoryPostRepository _posts;
        private readonly IRequestRepository _requests;
        private readonly IRequestarrUserRepository _users;
        private readonly IRequestFeed _feed;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IHistorySettings _settings;
        private readonly IAuditService _audit;
        private readonly IManageCommandQueue _commands;
        private readonly Logger _logger;
        private readonly object _lock = new();
        private readonly object _queueing = new();
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
                              IHistorySettings settings,
                              IAuditService audit,
                              IManageCommandQueue commands,
                              Logger logger)
        {
            _posts = posts;
            _requests = requests;
            _users = users;
            _feed = feed;
            _gateway = gateway;
            _configs = configs;
            _settings = settings;
            _audit = audit;
            _commands = commands;
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

        public void QueueImport(int afterRequestId) =>
            Queue(now => FeedRows(_requests.All().Where(r => r.Imported && r.Id > afterRequestId), now));

        public ServiceResult PostMember(int userId, Actor actor)
        {
            var user = _users.Find(userId);

            if (user?.Status == UserStatus.Suspended)
            {
                return ServiceResult.Fail(DiscordText.HistorySuspended);
            }

            if (user is not { Status: UserStatus.Active, PrivateChannelId: not null })
            {
                return ServiceResult.Fail(DiscordText.HistoryNoChannel);
            }

            var imported = ImportedFor(userId);

            if (imported.Count == 0)
            {
                return ServiceResult.Fail(DiscordText.HistoryNone);
            }

            var queued = Queue(now => ChannelRows(imported, now));

            if (queued.Count > 0)
            {
                _audit.Record(actor, PostedAction, "user", userId.ToString(), DiscordText.HistoryAudit(queued.Count));
                return Queued(queued.Count);
            }

            return _posts.Waiting().Any(p => p.ChannelId == user.PrivateChannelId) ? StillPosting() : ServiceResult.Fail(DiscordText.HistoryAlreadyPosted);
        }

        public ServiceResult PostServer(Actor actor)
        {
            var imported = _requests.All().Where(r => r.Imported).ToList();

            if (imported.Count == 0)
            {
                return ServiceResult.Fail(DiscordText.HistoryNone);
            }

            var queued = Queue(now => FeedRows(imported, now).Concat(ChannelRows(imported, now)));

            if (queued.Count > 0)
            {
                // A request in the feed and in its requester's channel is one request to whoever asked.
                var requests = queued.Select(p => p.RequestId).Distinct().Count();
                _audit.Record(actor, PostedAction, "import", null, DiscordText.HistoryAudit(requests));
                return Queued(requests);
            }

            if (_posts.Waiting().Any())
            {
                return StillPosting();
            }

            return ServiceResult.Fail(_posts.All().Any() ? DiscordText.HistoryAlreadyPosted : DiscordText.HistoryNowhere);
        }

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
            if (message.History == OnboardHistory.Post || (message.History == OnboardHistory.AsSet && _settings.WhenOnboarding))
            {
                Queue(now => ChannelRows(ImportedFor(message.UserId), now));
            }
        }

        // What waited when Requestarr stopped carries on, its feed header not posted again. Last, after Servarr's command
        // queue has requeued what it kept, so System, then Tasks never gets the run twice.
        [EventHandleOrder(EventHandleOrder.Last)]
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

        private List<Request> ImportedFor(int userId) => _requests.All().Where(r => r.Imported && r.OwnerUserId == userId).ToList();

        private ServiceResult Queued(int requests) =>
            ServiceResult.Ok(DiscordText.HistoryQueued(requests, Status().State == HistoryPostingState.Paused));

        // Nothing new to queue, but some of it hasn't gone out yet.
        private ServiceResult StillPosting() =>
            ServiceResult.Ok(Status().State == HistoryPostingState.Paused ? DiscordText.HistoryPaused : DiscordText.HistoryUnderWay);

        // Queues the posts not queued before, one caller at a time: each is queued once, whoever asks at the same moment.
        // The rows queued (one time for all of them, so a restart knows them as one batch); posting starts either way.
        private List<HistoryPost> Queue(Func<DateTime, IEnumerable<HistoryPost>> posts)
        {
            List<HistoryPost> rows;

            lock (_queueing)
            {
                rows = posts(DateTime.UtcNow).ToList();
                var waiting = _posts.Waiting();
                _posts.InsertMany(rows);
                Grow(rows.Count, waiting);
            }

            Start();

            return rows;
        }

        // Each request's post in the admin feed, when that's switched on.
        private IEnumerable<HistoryPost> FeedRows(IEnumerable<Request> imported, DateTime now)
        {
            if (!_settings.InFeed || FeedChannel() is not { } feedChannel)
            {
                return Enumerable.Empty<HistoryPost>();
            }

            var known = _posts.Known(HistoryDestination.Feed, feedChannel);

            return imported.Where(r => !known.Contains(r.Id)).Select(r => Row(r, HistoryDestination.Feed, feedChannel, now));
        }

        // Each request's post in its requester's channel, for those who are onboarded.
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

        // A new batch once the last one is done: the page keeps the last one's count until then, and rows queued before it
        // join the one under way. What a stopped batch left waiting is part of the new one, and its feed header isn't posted
        // again while cards of it wait.
        private void Grow(int rows, List<HistoryPost> waiting)
        {
            lock (_lock)
            {
                if (rows > 0 && _state == HistoryPostingState.Idle && !_running)
                {
                    _total = waiting.Count;
                    _posted = 0;

                    if (waiting.All(p => p.Destination != HistoryDestination.Feed))
                    {
                        _feedStarted = false;
                        _feedPosted = 0;
                    }
                }

                _total += rows;
            }
        }

        // Wakes the worker, or starts it; a pause holds everything until Resume.
        private void Start()
        {
            bool follow;
            bool start;

            lock (_lock)
            {
                if (_state == HistoryPostingState.Paused || !_posts.Waiting().Any())
                {
                    return;
                }

                // A resume shows the run under System, then Tasks again, even while the worker is still winding down.
                follow = _state != HistoryPostingState.Posting;
                _state = HistoryPostingState.Posting;
                start = !_running;
                _again = _running;
                _running = true;
            }

            if (follow)
            {
                _commands.Push(new PostImportedHistoryCommand());
            }

            if (start)
            {
                _ = Background(WorkAsync);
            }
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
                // Discord down, say: what waits stays queued, and Post History (or a restart) carries on.
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

        // Posts what waits, read afresh each pass so rows queued meanwhile go too; members' channels first, then the feed. A
        // member's channel that can't be posted in doesn't hold up anyone else: it's tried again on the next run.
        private async Task DrainAsync()
        {
            var failed = new HashSet<string>();

            while (Continuing() && _posts.Waiting().Where(p => !failed.Contains(p.ChannelId)).ToList() is { Count: > 0 } waiting)
            {
                var requests = _requests.Get(waiting.Select(p => p.RequestId).Distinct()).ToDictionary(r => r.Id);
                var members = waiting.Where(p => p.Destination == HistoryDestination.Channel)
                    .GroupBy(p => p.ChannelId).ToList();

                foreach (var member in members)
                {
                    if (!await TryPostChannelAsync(member.Key, member.ToList(), requests))
                    {
                        failed.Add(member.Key);
                    }
                }

                var feed = waiting.Where(p => p.Destination == HistoryDestination.Feed)
                    .OrderBy(p => requests.GetValueOrDefault(p.RequestId)?.Requested).ThenBy(p => p.RequestId).ToList();
                await PostFeedAsync(feed, requests);
            }

            Wrap();
        }

        private async Task<bool> TryPostChannelAsync(string channelId, List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            try
            {
                await PostChannelAsync(ulong.Parse(channelId), posts, requests);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Posting imported history in channel {0} failed; it's tried again on the next run", channelId);
                return false;
            }
        }

        // A member's history in their channel, oldest first, the batch's first card saying what they are.
        private async Task PostChannelAsync(ulong channel, List<HistoryPost> posts, IReadOnlyDictionary<int, Request> requests)
        {
            if (Unsendable(posts, requests))
            {
                return;
            }

            var ordered = posts.OrderBy(p => requests[p.RequestId].Requested).ThenBy(p => p.RequestId).ToList();

            var hasStarted = IsUnderWay(ordered);

            foreach (var post in ordered.TakeWhile(_ => Continuing()))
            {
                var card = RequestCards.HistoryCard(requests[post.RequestId], hasStarted ? null : DiscordText.HistoryChannelHeader);
                hasStarted |= await PostAsync(post, () => _gateway.SendMessageAsync(channel, card));
            }
        }

        // Whether some of these posts' batch went out in their channel before a pause or a restart.
        private bool IsUnderWay(List<HistoryPost> posts)
        {
            var batches = posts.Select(p => p.Queued).ToHashSet();

            return _posts.All().Any(p => p.Posted != null && p.ChannelId == posts[0].ChannelId && batches.Contains(p.Queued));
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
                if (await PostAsync(post, () => _feed.PostImportedAsync(requests[post.RequestId])))
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

                lock (_lock)
                {
                    _total -= gone.Count;
                }
            }

            return posts.Count == 0;
        }

        // One post after the pace's wait, recorded on its row before the next; false when paused or cancelled first.
        private async Task<bool> PostAsync(HistoryPost row, Func<Task<ulong>> send)
        {
            await Wait(Pace);

            if (!Continuing())
            {
                return false;
            }

            var id = await send();

            row.Posted = DateTime.UtcNow;
            row.MessageId = id == 0 ? null : id.ToString();
            _posts.Update(row);

            lock (_lock)
            {
                _posted++;
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
