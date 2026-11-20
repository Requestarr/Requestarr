using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // What everyone picked gets, per content type: their access, a limit (requests per so many days; null keeps the
    // server's) and an instance (null keeps the server's).
    public record OnboardAccess(ContentType ContentType, AccessState State, int? Limit, int? Days, int? InstanceId);

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
        OnboardJob Start(IReadOnlyList<OnboardPerson> people, IReadOnlyList<OnboardAccess> access, Actor admin);

        // Null once it's forgotten (Requestarr restarted) or never was.
        OnboardJob Find(string id);

        // Done when the run has gone through everyone.
        Task WhenDoneAsync(string id);
    }

    public class PortalOnboarding : IPortalOnboarding
    {
        // Runs kept for the page to read back; older ones are forgotten.
        private const int Kept = 20;

        private readonly ConcurrentDictionary<string, Run> _runs = new();
        private readonly IUserManagementService _users;
        private readonly IBackendRepository _backends;
        private readonly Logger _logger;

        public PortalOnboarding(IUserManagementService users, IBackendRepository backends, Logger logger)
        {
            _users = users;
            _backends = backends;
            _logger = logger;
        }

        // Between two people: Discord limits how quickly a server's channels and roles are created. Tests set it to zero.
        public TimeSpan Pace { get; set; } = TimeSpan.FromSeconds(2);

        public OnboardJob Start(IReadOnlyList<OnboardPerson> people, IReadOnlyList<OnboardAccess> access, Actor admin)
        {
            var run = new Run(Guid.NewGuid().ToString("N"), people);
            _runs[run.Id] = run;
            Forget();
            run.Task = Task.Run(() => OnboardAllAsync(run, access, admin));

            return run.Snapshot();
        }

        public OnboardJob Find(string id) => id != null && _runs.TryGetValue(id, out var run) ? run.Snapshot() : null;

        public Task WhenDoneAsync(string id) => id != null && _runs.TryGetValue(id, out var run) ? run.Task : Task.CompletedTask;

        private async Task OnboardAllAsync(Run run, IReadOnlyList<OnboardAccess> access, Actor admin)
        {
            for (var i = 0; i < run.Count; i++)
            {
                if (i > 0 && Pace > TimeSpan.Zero)
                {
                    await Task.Delay(Pace);
                }

                var person = run.Person(i);
                run.Set(i, OnboardState.Adding, null);
                var (done, message) = await OnboardAsync(person, access, admin);
                run.Set(i, done ? OnboardState.Added : OnboardState.Failed, message);
            }

            run.Finish();
        }

        // The member card's onboarding with the access picked, then each type's own instance and limit where one was
        // picked. Anything that goes wrong is this person's failure, said in Requestarr's words.
        private async Task<(bool Done, string Message)> OnboardAsync(OnboardPerson person, IReadOnlyList<OnboardAccess> access, Actor admin)
        {
            if (!ulong.TryParse(person.DiscordId, out var id))
            {
                return (false, "Pick someone from the server.");
            }

            try
            {
                var added = await _users.AddAsync(id, access.ToDictionary(a => a.ContentType, a => a.State), admin);

                if (!added.Success)
                {
                    return (false, added.Message);
                }

                foreach (var type in access)
                {
                    SetOwn(id, type, admin);
                }

                return (true, added.Message);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Onboarding {0} from the web app failed", person.Name);
                return (false, $"Something went wrong: {ex.Message}");
            }
        }

        private void SetOwn(ulong id, OnboardAccess type, Actor admin)
        {
            if (type.InstanceId is { } instanceId && _backends.Find(instanceId) is { } instance)
            {
                _users.SetAccess(id, type.ContentType, type.State, admin, instance.Name);
            }

            if (type.Limit is { } limit && type.Days is { } days)
            {
                _users.SetQuota(id, type.ContentType, limit, days, admin);
            }
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
            private bool _isDone;

            public Run(string id, IReadOnlyList<OnboardPerson> people)
            {
                Id = id;
                _people = people.Select(p => new OnboardResult(p.DiscordId, p.Name, OnboardState.Waiting, null)).ToArray();
            }

            public string Id { get; }

            public DateTime Started { get; } = DateTime.UtcNow;

            public Task Task { get; set; } = Task.CompletedTask;

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
