using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Instrumentation.Extensions;
using NzbDrone.Core.Messaging.Commands;
using Requestarr.Core.Audit;
using Requestarr.Core.Imports;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // What everyone picked gets, per content type: their access and a limit (requests per so many days; 0 for none, null
    // leaves the one they had).
    public record OnboardAccess(ContentType ContentType, AccessState State, int? Limit, int? Days);

    public record OnboardPerson(string DiscordId, string Name);

    public enum OnboardState
    {
        Waiting,
        Adding,
        Added,
        Failed
    }

    // One person's part: where it's got to, and Requestarr's words for how it went (why it failed).
    public record OnboardResult(string DiscordId, string Name, OnboardState State, string Message);

    public record OnboardJob(string Id, IReadOnlyList<OnboardResult> People, bool IsDone);

    // Onboarding several people at once from the web app's Add member: each goes through the member card's onboarding
    // (role, private channel, pinned panel, welcome), one after another, paced for Discord's limits on creating channels
    // and roles. The page asks how it's going; one failure doesn't stop the rest.
    public interface IPortalOnboarding
    {
        OnboardJob Start(IReadOnlyList<OnboardPerson> people, IReadOnlyList<OnboardAccess> access, OnboardHistory history, Actor admin);

        // Null once it's forgotten (Requestarr restarted) or never was.
        OnboardJob Find(string id);

        // Done when the run has gone through everyone.
        Task WhenDoneAsync(string id);
    }

    public class PortalOnboarding : IPortalOnboarding, IExecute<OnboardMembersCommand>
    {
        // Runs kept for the page to read back; older ones are forgotten.
        private const int Kept = 20;

        // A limit without its days counts a week, as the member card's limit does.
        private const int DefaultDays = 7;

        private readonly ConcurrentDictionary<string, Run> _runs = new();
        private readonly IUserManagementService _users;
        private readonly IManageCommandQueue _commands;
        private readonly Logger _logger;

        public PortalOnboarding(IUserManagementService users, IManageCommandQueue commands, Logger logger)
        {
            _users = users;
            _commands = commands;
            _logger = logger;
        }

        // Between two people: Discord limits how quickly a server's channels and roles are created. Tests set it to zero.
        public TimeSpan Pace { get; set; } = TimeSpan.FromSeconds(2);

        public OnboardJob Start(IReadOnlyList<OnboardPerson> people, IReadOnlyList<OnboardAccess> access, OnboardHistory history, Actor admin)
        {
            var run = new Run(Guid.NewGuid().ToString("N"), people) { Access = access, History = history, Admin = admin };
            _runs[run.Id] = run;
            Forget();
            _commands.Push(new OnboardMembersCommand { RunId = run.Id }, trigger: CommandTrigger.Manual);

            return run.Snapshot();
        }

        // The run Start queued, on the command queue's thread; a run already forgotten is skipped.
        public void Execute(OnboardMembersCommand message)
        {
            if (_runs.TryGetValue(message.RunId ?? string.Empty, out var run))
            {
                _logger.ProgressInfo("Onboarding {0} people", run.Count);
                OnboardAllAsync(run).GetAwaiter().GetResult();
            }
        }

        public OnboardJob Find(string id) => id != null && _runs.TryGetValue(id, out var run) ? run.Snapshot() : null;

        public Task WhenDoneAsync(string id) => id != null && _runs.TryGetValue(id, out var run) ? run.Done : Task.CompletedTask;

        private async Task OnboardAllAsync(Run run)
        {
            for (var i = 0; i < run.Count; i++)
            {
                if (i > 0 && Pace > TimeSpan.Zero)
                {
                    await Task.Delay(Pace);
                }

                var person = run.Person(i);
                run.Set(i, OnboardState.Adding, null);
                var (done, message) = await OnboardAsync(person, run);
                run.Set(i, done ? OnboardState.Added : OnboardState.Failed, message);
            }

            run.Finish();
        }

        // The member card's onboarding with the access picked, then each type's own instance and limit where one was
        // picked. The add going wrong is this person's failure, in Requestarr's words; once added they're Added, and an
        // instance or limit that didn't apply is said beside it (Retry would only find them a member already).
        private async Task<(bool Done, string Message)> OnboardAsync(OnboardPerson person, Run run)
        {
            if (!ulong.TryParse(person.DiscordId, out var id))
            {
                return (false, "Pick someone from the server.");
            }

            ServiceResult added;

            try
            {
                added = await _users.AddAsync(id, run.Access.ToDictionary(a => a.ContentType, a => a.State), run.History, run.Admin);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Onboarding {0} from the web app failed", person.Name);
                return (false, $"Something went wrong: {ex.Message}");
            }

            if (!added.Success)
            {
                return (false, added.Message);
            }

            var notSet = run.Access.SelectMany(a => SetOwn(id, a, run.Admin)).ToList();

            return (true, string.Join(" ", new[] { added.Message }.Concat(notSet)));
        }

        // What didn't apply of this type's own limit, in Requestarr's words.
        private IEnumerable<string> SetOwn(ulong id, OnboardAccess typeAccess, Actor admin)
        {
            var results = new List<ServiceResult>();

            try
            {
                if (typeAccess.Limit is { } limit)
                {
                    results.Add(_users.SetQuota(id, typeAccess.ContentType, limit, typeAccess.Days ?? DefaultDays, admin));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Setting {0} for a member onboarded from the web app failed", typeAccess.ContentType);
                results.Add(ServiceResult.Fail($"Something went wrong: {ex.Message}"));
            }

            return results.Where(r => !r.Success).Select(r => $"Not set for {typeAccess.ContentType}: {r.Message}");
        }

        private void Forget()
        {
            foreach (var old in _runs.Values.OrderByDescending(r => r.Started).Skip(Kept).ToList())
            {
                _runs.TryRemove(old.Id, out _);
            }
        }

        // One run, read by the page while it goes on: each person's part changes under a lock.
        private sealed class Run
        {
            private readonly object _lock = new();
            private readonly OnboardResult[] _people;
            private readonly TaskCompletionSource _finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private bool _isDone;

            public Run(string id, IReadOnlyList<OnboardPerson> people)
            {
                Id = id;
                _people = people.Select(p => new OnboardResult(p.DiscordId, p.Name, OnboardState.Waiting, null)).ToArray();
            }

            public string Id { get; }

            public DateTime Started { get; } = DateTime.UtcNow;

            // Finished, for a test to wait on.
            public Task Done => _finished.Task;

            public IReadOnlyList<OnboardAccess> Access { get; init; }

            public OnboardHistory History { get; init; }

            public Actor Admin { get; init; }

            public int Count => _people.Length;

            public OnboardPerson Person(int index) => new(_people[index].DiscordId, _people[index].Name);

            public void Set(int index, OnboardState state, string message)
            {
                lock (_lock)
                {
                    _people[index] = _people[index] with { State = state, Message = message };
                }
            }

            public void Finish()
            {
                lock (_lock)
                {
                    _isDone = true;
                }

                _finished.TrySetResult();
            }

            public OnboardJob Snapshot()
            {
                lock (_lock)
                {
                    return new OnboardJob(Id, _people.ToList(), _isDone);
                }
            }
        }
    }
}
