using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Imports
{
    public enum ImportJobState
    {
        Reading,
        Ready,
        Failed,
        Imported
    }

    public record ImportProgress([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong ChannelId, string Name, int Read, bool Done);

    public record ImportResult(int Requests, int Followers, int People);

    // An import as the page follows it: reading the channels (with how far each has got), then the preview, then what
    // was written. Problem: why it stopped, in words. Warning: something the preview may be missing because of it.
    public record ImportJobView(Guid Id, ImportJobState State, IReadOnlyList<ImportProgress> Channels, string Problem, string Warning, ImportPlan Plan, ImportResult Result);

    public interface IImportService
    {
        Task<ImportChannel> DescribeAsync(ulong channelId);
        Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync();

        // Checks every channel, then reads them in the background; nothing is written until ConfirmAsync.
        Task<ImportJobView> StartPreviewAsync(IReadOnlyList<ulong> channelIds);
        ImportJobView Job(Guid id);

        // Writes the preview, worked out again against what Requestarr has now (an earlier run, a request made since).
        Task<ImportJobView> ConfirmAsync(Guid id, Actor admin);
    }

    // Imports past requests from another request bot's channels (Requestrr): who asked for what, when, and how it ended,
    // matched to Sonarr and Radarr by ID. Discord is only read; nothing is posted anywhere, here or there.
    public class ImportService : IImportService
    {
        public const string ImportAction = "import.requestrr";
        public const string ImportedAction = "request.imported";

        private readonly ConcurrentDictionary<Guid, PreviewJob> _jobs = new();
        private readonly IChannelHistory _history;
        private readonly IImportSource _source;
        private readonly IBackendService _backends;
        private readonly IRequestRepository _requests;
        private readonly IRequestSubscriberRepository _subscribers;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserManagementService _users;
        private readonly IImportedMessageRepository _imported;
        private readonly IAuditRepository _auditLog;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public ImportService(IChannelHistory history,
                             IBackendService backends,
                             IRequestRepository requests,
                             IRequestSubscriberRepository subscribers,
                             IUserIdentityRepository identities,
                             IUserManagementService users,
                             IImportedMessageRepository imported,
                             IAuditRepository auditLog,
                             IAuditService audit,
                             Logger logger)
        {
            _history = history;
            _source = new RequestrrSource();
            _backends = backends;
            _requests = requests;
            _subscribers = subscribers;
            _identities = identities;
            _users = users;
            _imported = imported;
            _auditLog = auditLog;
            _audit = audit;
            _logger = logger;
        }

        public Task<ImportChannel> DescribeAsync(ulong channelId) => _history.DescribeAsync(channelId);

        public Task<IReadOnlyList<ImportChannel>> ReadableChannelsAsync() => _history.ReadableChannelsAsync();

        public async Task<ImportJobView> StartPreviewAsync(IReadOnlyList<ulong> channelIds)
        {
            var channels = new List<ImportChannel>();

            foreach (var id in channelIds.Distinct())
            {
                channels.Add(await _history.DescribeAsync(id));
            }

            var job = new PreviewJob(Guid.NewGuid(), channels);
            _jobs[job.Id] = job;

            var problem = channelIds.Count == 0 ? ImportText.NoChannels : channels.FirstOrDefault(c => !c.CanRead)?.Problem;

            if (problem != null)
            {
                job.Fail(problem);
                return job.View();
            }

            _ = Task.Run(() => ReadAsync(job));
            return job.View();
        }

        public ImportJobView Job(Guid id) => _jobs.TryGetValue(id, out var job) ? job.View() : null;

        public async Task<ImportJobView> ConfirmAsync(Guid id, Actor admin)
        {
            if (!_jobs.TryGetValue(id, out var job) || job.State != ImportJobState.Ready)
            {
                return job?.View();
            }

            await job.Writing.WaitAsync();

            try
            {
                var plan = ImportPlanner.Plan(job.Messages, Context(job.Library));
                job.Done(plan, Write(plan, job.Channels, admin));
                return job.View();
            }
            finally
            {
                job.Writing.Release();
            }
        }

        private async Task ReadAsync(PreviewJob job)
        {
            try
            {
                foreach (var channel in job.Channels)
                {
                    await ReadChannelAsync(job, channel);
                }

                job.Library = await LibraryAsync();
                job.Ready(ImportPlanner.Plan(job.Messages, Context(job.Library)));
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Import preview failed");
                job.Fail($"Reading stopped: {ex.Message}");
            }
        }

        // Page by page, newest first, until the channel's start; Discord.Net waits out Discord's rate limits.
        private async Task ReadChannelAsync(PreviewJob job, ImportChannel channel)
        {
            ulong? before = null;

            while (await _history.ReadAsync(channel.Id, before) is { Count: > 0 } page)
            {
                job.Read(channel.Id, page.Select(_source.Read).ToList(), page.Any(m => m.LooksWithheld));
                before = page.Min(m => m.Id);
            }

            job.Finished(channel.Id);
        }

        private async Task<IReadOnlyList<LibraryEntry>> LibraryAsync()
        {
            var entries = new List<LibraryEntry>();

            foreach (var instance in _backends.All().Where(i => i.Enable && i.Implementation is BackendImplementations.Radarr or BackendImplementations.Sonarr))
            {
                var type = instance.Implementation == BackendImplementations.Radarr ? ContentType.Movie : ContentType.Tv;

                try
                {
                    entries.AddRange((await _backends.ApiFor(instance).GetLibraryAsync(instance.Settings)).Select(t => new LibraryEntry(instance.Id, instance.Name, type, t)));
                }
                catch (BackendException ex)
                {
                    throw new BackendException($"couldn't read {instance.Name}'s library ({ex.Message})");
                }
            }

            return entries;
        }

        private ImportContext Context(IReadOnlyList<LibraryEntry> library) => new(
            _imported.MessageIds(),
            library,
            _requests.All().ToList(),
            _subscribers.All().ToLookup(s => s.RequestId, s => s.UserId),
            _identities.All().Where(i => i.Provider == UserIdentity.DiscordProvider && ulong.TryParse(i.ExternalId, out _))
                .GroupBy(i => ulong.Parse(i.ExternalId)).ToDictionary(g => g.Key, g => g.First().UserId));

        private ImportResult Write(ImportPlan plan, IReadOnlyList<ImportChannel> channels, Actor admin)
        {
            var people = new PeopleRecords(_users);
            var names = channels.ToDictionary(c => c.Id, c => c.Name);
            var followers = 0;

            foreach (var planned in plan.Requests)
            {
                var request = _requests.Insert(NewRequest(planned, people.For(planned.Requester)));
                History(planned.Requester, people.For(planned.Requester), ImportedAction, request.Id, planned.Requested, $"From {_source.Name}, #{names.GetValueOrDefault(planned.ChannelId)}");

                foreach (var follower in planned.Followers)
                {
                    Follow(request.Id, follower, people);
                    followers++;
                }

                if (planned.Available is { } available)
                {
                    _auditLog.Insert(new AuditEntry { Actor = Actor.System.Description, Action = "request.fulfilled", TargetType = "request", TargetId = request.Id.ToString(), Timestamp = available, Detail = $"Finished downloading, as {_source.Name} announced" });
                }
            }

            foreach (var follow in plan.Follows)
            {
                Follow(follow.RequestId, follow.Follower, people);
                followers++;
            }

            var now = DateTime.UtcNow;
            _imported.InsertMany(plan.Recorded.Select(m => new ImportedMessage { MessageId = m.MessageId.ToString(), ChannelId = m.ChannelId.ToString(), Source = _source.Name, Imported = now }).ToList());

            var result = new ImportResult(plan.Requests.Count, followers, people.Created);
            var detail = $"{result.Requests} requests and {result.Followers} followers from {_source.Name} ({string.Join(", ", channels.Select(c => "#" + c.Name))}); " +
                         $"{plan.Skipped.Count} skipped, {plan.Unmatched.Count} not in Sonarr or Radarr, {result.People} people not members yet";
            _audit.Record(admin, ImportAction, "import", null, detail);
            _logger.Info("Imported {0} requests and {1} followers from {2}", result.Requests, result.Followers, _source.Name);

            return result;
        }

        private static Request NewRequest(PlannedRequest planned, RequestarrUser owner)
        {
            var available = planned.Available.HasValue || planned.HasFiles;
            var everySeason = planned.Type == ContentType.Tv && planned.Seasons.Count == 0;
            var seasons = everySeason ? planned.LibrarySeasons : planned.Seasons;

            return new Request
            {
                OwnerUserId = owner.Id,
                ContentType = planned.Type,
                Title = planned.Title,
                Year = planned.Year,
                TmdbId = planned.TmdbId,
                TvdbId = planned.TvdbId,
                BackendId = planned.InstanceId,
                ExternalItemId = planned.ItemId,
                Seasons = planned.Type == ContentType.Tv ? seasons.ToList() : new List<int>(),
                MonitorFutureSeasons = everySeason,
                Status = available ? RequestStatus.Available : RequestStatus.Approved,
                Progress = available ? DownloadProgress.Imported : DownloadProgress.None,
                Requested = planned.Requested,
                Decided = planned.Requested,
                Fulfilled = available ? planned.Available ?? planned.Requested : null
            };
        }

        private void Follow(int requestId, ImportFollower follower, PeopleRecords people)
        {
            var user = people.For(follower.Person);
            _subscribers.Insert(new RequestSubscriber { RequestId = requestId, UserId = user.Id, Added = follower.Asked });
            History(follower.Person, user, "request.subscribe", requestId, follower.Asked, $"From {_source.Name}");
        }

        // The request's history, dated when it happened in the old channel and by whoever did it.
        private void History(ImportPerson person, RequestarrUser user, string action, int requestId, DateTime at, string detail) =>
            _auditLog.Insert(new AuditEntry { ActorUserId = user.Id, Actor = Actor.Discord(person.DiscordId, person.Name, user.Id).Description, Action = action, TargetType = "request", TargetId = requestId.ToString(), Timestamp = at, Detail = detail });

        // Members as they are; anyone else recorded once as someone Requestarr knows, shown by their Discord name, who
        // becomes a member (with these requests) if an admin adds them.
        private sealed class PeopleRecords
        {
            private readonly IUserManagementService _users;
            private readonly Dictionary<ulong, RequestarrUser> _seen = new();

            public PeopleRecords(IUserManagementService users)
            {
                _users = users;
            }

            public int Created { get; private set; }

            public RequestarrUser For(ImportPerson person)
            {
                if (_seen.TryGetValue(person.DiscordId, out var user))
                {
                    return user;
                }

                user = _users.FindByDiscordId(person.DiscordId);

                if (user == null)
                {
                    user = _users.EnsureRecord(person.DiscordId, person.Name, person.Name);
                    Created++;
                }

                _seen[person.DiscordId] = user;
                return user;
            }
        }

        private sealed class PreviewJob
        {
            private readonly object _lock = new();
            private readonly Dictionary<ulong, ImportProgress> _progress;
            private readonly List<SourceMessage> _messages = new();
            private string _problem;
            private string _warning;
            private ImportPlan _plan;
            private ImportResult _result;

            public PreviewJob(Guid id, IReadOnlyList<ImportChannel> channels)
            {
                Id = id;
                Channels = channels;
                _progress = channels.ToDictionary(c => c.Id, c => new ImportProgress(c.Id, c.Name, 0, false));
            }

            public Guid Id { get; }
            public IReadOnlyList<ImportChannel> Channels { get; }
            public ImportJobState State { get; private set; } = ImportJobState.Reading;
            public IReadOnlyList<LibraryEntry> Library { get; set; } = Array.Empty<LibraryEntry>();
            public System.Threading.SemaphoreSlim Writing { get; } = new(1, 1);

            public IReadOnlyList<SourceMessage> Messages
            {
                get
                {
                    lock (_lock)
                    {
                        return _messages.ToList();
                    }
                }
            }

            public void Read(ulong channel, IReadOnlyList<SourceMessage> page, bool withheld)
            {
                lock (_lock)
                {
                    _messages.AddRange(page);
                    _progress[channel] = _progress[channel] with { Read = _progress[channel].Read + page.Count };
                    _warning = withheld ? ImportText.NoMessageContent : _warning;
                }
            }

            public void Finished(ulong channel)
            {
                lock (_lock)
                {
                    _progress[channel] = _progress[channel] with { Done = true };
                }
            }

            public void Ready(ImportPlan plan) => Set(ImportJobState.Ready, plan, null, null);

            public void Done(ImportPlan plan, ImportResult result) => Set(ImportJobState.Imported, plan, result, null);

            public void Fail(string problem) => Set(ImportJobState.Failed, null, null, problem);

            public ImportJobView View()
            {
                lock (_lock)
                {
                    return new ImportJobView(Id, State, Channels.Select(c => _progress[c.Id]).ToList(), _problem, _warning, _plan, _result);
                }
            }

            private void Set(ImportJobState state, ImportPlan plan, ImportResult result, string problem)
            {
                lock (_lock)
                {
                    State = state;
                    _plan = plan ?? _plan;
                    _result = result;
                    _problem = problem;
                }
            }
        }
    }
}
