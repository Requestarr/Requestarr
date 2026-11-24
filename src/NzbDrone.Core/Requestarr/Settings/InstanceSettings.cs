using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Users;

namespace Requestarr.Core.Settings
{
    // A Sonarr or Radarr as the settings pages edit it. ApiKey left empty on a change keeps the saved one; the anime
    // terms are comma-separated, empty for Requestarr's defaults. BrowserUrl: optional, where an admin's browser reaches
    // it, for links to its pages. Purpose: what it's for (InstancePurpose). DefaultAccess: what a new member gets for its
    // type (Needs approval when left out, as before the setting existed).
    public record InstanceInput(string Implementation, string Name, string Url, string ApiKey, string QualityProfile, string RootFolder, string SeriesType,
                                InstancePurpose Purpose, bool Enabled, bool PollingOnly, bool AnimeEnabled, string AnimePriority,
                                string DubRequired, string DubIgnored, string SubRequired, string SubIgnored, string BrowserUrl = null,
                                AccessState DefaultAccess = AccessState.NeedsApproval);

    // The API key is never shown back, only whether one is saved.
    public record InstanceView(int Id, InstanceInput Settings, bool ApiKeySet);

    public record InstanceTestResult(string Problem, IReadOnlyList<string> QualityProfiles, IReadOnlyList<string> RootFolders);

    public record InstanceSaveResult(InstanceView Instance, string Problem);

    public interface IInstanceSettingsService
    {
        IReadOnlyList<InstanceView> All();

        // Reaches the instance before it's saved and lists its quality profiles and root folders. Id: the saved
        // instance whose API key to use when none is given and the address is its own.
        Task<InstanceTestResult> TestAsync(int? id, InstanceInput input);
        Task<InstanceSaveResult> SaveAsync(int? id, InstanceInput input, Actor actor);
    }

    // Sonarr and Radarr instances for beta and production, where there is no provisioning file. Deleting one keeps its
    // history (InstanceDeletion); turning one off pauses it.
    public class InstanceSettingsService : IInstanceSettingsService
    {
        public const string AddAction = "instance.add";
        public const string ChangeAction = "instance.change";

        private const string ApiKeyLabel = "API key";

        private static readonly string[] Implementations = { BackendImplementations.Sonarr, BackendImplementations.Radarr };

        private readonly IBackendService _backends;
        private readonly IBackendRepository _repository;
        private readonly IProvisioningService _provisioning;
        private readonly IAuditService _audit;

        public InstanceSettingsService(IBackendService backends, IBackendRepository repository, IProvisioningService provisioning, IAuditService audit)
        {
            _backends = backends;
            _repository = repository;
            _provisioning = provisioning;
            _audit = audit;
        }

        public IReadOnlyList<InstanceView> All() =>
            _backends.All().Where(b => Implementations.Contains(b.Implementation)).OrderBy(b => b.Implementation).ThenBy(b => b.Name).Select(View).ToList();

        public async Task<InstanceTestResult> TestAsync(int? id, InstanceInput input)
        {
            if (!Implementations.Contains(input.Implementation))
            {
                return new InstanceTestResult("Choose which app this connection is.", Array.Empty<string>(), Array.Empty<string>());
            }

            // The saved API key is only sent to the saved address: a changed address needs its key typed again.
            var saved = id is { } savedId ? _backends.Find(savedId)?.Config : null;
            var settings = Settings(input, saved?.Url == input.Url?.Trim() ? saved : null);

            if (Missing(input, settings) is { } missing)
            {
                return new InstanceTestResult(missing, Array.Empty<string>(), Array.Empty<string>());
            }

            try
            {
                var probe = await _backends.ProbeAsync(input.Implementation, settings);
                return new InstanceTestResult(probe.Error, probe.QualityProfiles.Select(p => p.Name).ToList(), probe.RootFolders);
            }
            catch (Exception ex)
            {
                return new InstanceTestResult(ConnectionProblems.Explain(input.Implementation, input.Url, ex), Array.Empty<string>(), Array.Empty<string>());
            }
        }

        public async Task<InstanceSaveResult> SaveAsync(int? id, InstanceInput input, Actor actor)
        {
            var existing = id is { } saved ? _backends.Find(saved) : null;

            if ((Problem(id, existing, input) ?? AnimeProblem(input)) is { } problem)
            {
                return new InstanceSaveResult(null, problem);
            }

            var others = Others(input.Implementation, id);
            var resolved = input with { Purpose = others.Any() ? input.Purpose : InstancePurpose.Everything };
            var instance = existing ?? new BackendInstance { Added = DateTime.UtcNow };
            var before = existing == null ? null : Described(existing);

            instance.Name = input.Name.Trim();
            instance.Implementation = input.Implementation;
            instance.Enable = input.Enabled;
            instance.Settings = Settings(resolved, existing?.Config);
            _repository.Upsert(instance);
            TakePurpose(instance, others, actor);

            Record(actor, instance, before);
            await _backends.CheckAllAsync();

            return new InstanceSaveResult(View(_backends.Find(instance.Id)), null);
        }

        // What the form still needs before Requestarr can reach the instance.
        private static string Missing(InstanceInput input, BackendSettings settings)
        {
            if (string.IsNullOrWhiteSpace(input.Url))
            {
                return ConnectionProblems.NoAddress;
            }

            return string.IsNullOrWhiteSpace(settings.ApiKey) ? ConnectionProblems.NoApiKey(input.Implementation) : null;
        }

        private string Problem(int? id, BackendInstance existing, InstanceInput input)
        {
            if (_provisioning.UsesFile)
            {
                return AppSettingsService.ProvisionedProblem;
            }

            if (id.HasValue && existing == null)
            {
                return "That instance no longer exists.";
            }

            if (!Implementations.Contains(input.Implementation) || (existing != null && existing.Implementation != input.Implementation))
            {
                return "Choose which app this connection is; a saved one keeps its app.";
            }

            if (string.IsNullOrWhiteSpace(input.Name) || (_backends.FindByName(input.Name.Trim()) is { } named && named.Id != id))
            {
                return "Give the instance a name no other instance has.";
            }

            if (!AppSettingsService.IsWebAddress(input.Url?.Trim()))
            {
                return "The address starts with http:// or https://, for example http://192.168.1.10:8989.";
            }

            if (!string.IsNullOrWhiteSpace(input.BrowserUrl) && !AppSettingsService.IsWebAddress(input.BrowserUrl.Trim()))
            {
                return "The address for links starts with http:// or https://, for example https://tv.example.com.";
            }

            if (!Enum.IsDefined(input.DefaultAccess))
            {
                return "Choose Approved automatically, Needs approval or Off for new members.";
            }

            return existing == null && string.IsNullOrWhiteSpace(input.ApiKey) ? $"Paste the API key from {input.Implementation}'s Settings, General." : null;
        }

        private static string AnimeProblem(InstanceInput input) =>
            input.Purpose == InstancePurpose.Anime && input.Implementation != BackendImplementations.Sonarr ? "Anime is for Sonarr only." : null;

        // The type's other instances that aren't deleted.
        private List<BackendInstance> Others(string implementation, int? id) =>
            _backends.All().Where(b => b.Implementation == implementation && b.Id != id).ToList();

        // Each purpose belongs to one instance per type: saving it here moves it from the instance that had it. Turning
        // off the one for all other requests, or giving it another purpose, hands that purpose to nobody: those requests
        // are paused (BackendService.ForRequest) and the admin queue says so until an admin gives it to another.
        private void TakePurpose(BackendInstance instance, IEnumerable<BackendInstance> others, Actor actor)
        {
            if (!instance.Enable || instance.Config.Purpose == InstancePurpose.Nothing)
            {
                return;
            }

            foreach (var other in others.Where(o => o.Config.Purpose == instance.Config.Purpose))
            {
                other.Config.Purpose = InstancePurpose.Nothing;
                _repository.Update(other);
                _audit.Record(actor, ChangeAction, "instance", other.Id.ToString(), $"{other.Name}: for {Words(instance)} moved to {instance.Name}");
            }
        }

        public static string Words(BackendInstance instance) => instance.Config.Purpose switch
        {
            InstancePurpose.Everything => DiscordText.AllOtherLabel(DiscordText.TypeOf(instance)).ToLowerInvariant(),
            InstancePurpose.Anime => "anime",
            _ => "nothing yet"
        };

        private void Record(Actor actor, BackendInstance instance, IReadOnlyList<(string Label, string Value)> before)
        {
            var after = Described(instance);

            if (before == null)
            {
                _audit.Record(actor, AddAction, "instance", instance.Id.ToString(), $"{instance.Name}: {instance.Implementation} at {instance.Config.Url}");
                return;
            }

            var changes = after.Zip(before, Change).Where(c => c != null).ToList();

            if (changes.Any())
            {
                _audit.Record(actor, ChangeAction, "instance", instance.Id.ToString(), $"{instance.Name}: {string.Join("; ", changes)}");
            }
        }

        // The API key is compared but never written down.
        private static string Change((string Label, string Value) now, (string Label, string Value) then)
        {
            if (now.Value == then.Value)
            {
                return null;
            }

            return now.Label == ApiKeyLabel ? "API key replaced" : $"{now.Label} from {then.Value} to {now.Value}";
        }

        // What the change record compares, in the words it uses.
        private static IReadOnlyList<(string Label, string Value)> Described(BackendInstance instance)
        {
            var settings = instance.Config;

            return new List<(string, string)>
            {
                ("name", instance.Name),
                ("address", settings.Url),
                ("address for links", Shown(settings.BrowserUrl)),
                (ApiKeyLabel, settings.ApiKey),
                ("quality profile", Shown(settings.QualityProfile)),
                ("root folder", Shown(settings.RootFolder)),
                ("series type", Shown(settings.SeriesType)),
                ("for", Words(instance)),
                ("in use", OnOff(instance.Enable)),
                ("polling only", OnOff(settings.PollingOnly)),
                ("default access for new members", DiscordText.Label(settings.DefaultAccess)),
                ("anime sub or dub", OnOff(settings.AnimeEnabled)),
                ("anime priority", settings.AnimePriority),
                ("dub terms", $"{InstanceInputs.Terms(settings.DubRequired)} / {InstanceInputs.Terms(settings.DubIgnored)}"),
                ("sub terms", $"{InstanceInputs.Terms(settings.SubRequired)} / {InstanceInputs.Terms(settings.SubIgnored)}")
            };
        }

        private static BackendSettings Settings(InstanceInput input, BackendSettings saved)
        {
            var sonarr = input.Implementation == BackendImplementations.Sonarr;

            return new BackendSettings
            {
                Url = input.Url?.Trim(),
                BrowserUrl = Blank(input.BrowserUrl?.Trim()),
                ApiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? saved?.ApiKey : input.ApiKey.Trim(),
                QualityProfile = Blank(input.QualityProfile),
                RootFolder = Blank(input.RootFolder),
                SeriesType = sonarr ? Blank(input.SeriesType) : null,
                Purpose = input.Purpose,
                PollingOnly = input.PollingOnly,
                DefaultAccess = input.DefaultAccess,
                AnimeEnabled = !sonarr || input.AnimeEnabled,
                AnimePriority = AnimeDefaults.PriorityKey(AnimeDefaults.ParsePriority(input.AnimePriority)),
                DubRequired = TermList(input.DubRequired),
                DubIgnored = TermList(input.DubIgnored),
                SubRequired = TermList(input.SubRequired),
                SubIgnored = TermList(input.SubIgnored)
            };
        }

        private static InstanceView View(BackendInstance instance) =>
            new(instance.Id, InstanceInputs.Of(instance) with { ApiKey = null }, !string.IsNullOrEmpty(instance.Config.ApiKey));

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Shown(string value) => value ?? "none";

        private static string OnOff(bool value) => value ? "on" : "off";

        private static List<string> TermList(string terms)
        {
            var list = (terms ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return list.Any() ? list : null;
        }
    }

    // An instance as the settings rules take it: its saved settings, API key included, the anime terms comma-separated.
    public static class InstanceInputs
    {
        public static InstanceInput Of(BackendInstance instance)
        {
            var settings = instance.Config;

            return new InstanceInput(
                instance.Implementation,
                instance.Name,
                settings.Url,
                settings.ApiKey,
                settings.QualityProfile,
                settings.RootFolder,
                settings.SeriesType,
                settings.Purpose,
                instance.Enable,
                settings.PollingOnly,
                settings.AnimeEnabled,
                settings.AnimePriority,
                Terms(settings.DubRequired),
                Terms(settings.DubIgnored),
                Terms(settings.SubRequired),
                Terms(settings.SubIgnored),
                settings.BrowserUrl,
                settings.DefaultAccess);
        }

        public static string Terms(IEnumerable<string> terms) => terms == null ? string.Empty : string.Join(", ", terms);
    }
}
