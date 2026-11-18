using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Settings
{
    // A Sonarr or Radarr as the settings pages edit it. ApiKey left empty on a change keeps the saved one; the anime
    // terms are comma-separated, empty for Requestarr's defaults.
    public record InstanceInput(string Implementation, string Name, string Url, string ApiKey, string QualityProfile, string RootFolder, string SeriesType,
                                bool IsDefault, bool Enabled, bool PollingOnly, bool AnimeEnabled, string AnimePriority,
                                string DubRequired, string DubIgnored, string SubRequired, string SubIgnored);

    // The API key is never shown back, only whether one is saved. Problem: why the last check failed, or null.
    public record InstanceView(int Id, InstanceInput Settings, bool ApiKeySet, bool Connected, string Problem);

    public record InstanceTestResult(string Problem, IReadOnlyList<string> QualityProfiles, IReadOnlyList<string> RootFolders);

    public record InstanceSaveResult(InstanceView Instance, string Problem);

    public interface IInstanceSettingsService
    {
        IReadOnlyList<InstanceView> All();

        // Reaches the instance before it's saved and lists its quality profiles and root folders. Id: the saved
        // instance whose API key to use when none is given.
        Task<InstanceTestResult> TestAsync(int? id, InstanceInput input);
        Task<InstanceSaveResult> SaveAsync(int? id, InstanceInput input, Actor actor);
    }

    // Sonarr and Radarr instances for beta and production, where there is no provisioning file. An instance is never
    // deleted, since requests keep their history with it: it's turned off instead.
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
                return new InstanceTestResult("Choose Sonarr or Radarr.", Array.Empty<string>(), Array.Empty<string>());
            }

            var settings = Settings(input, id is { } saved ? _backends.Find(saved)?.Settings : null);

            try
            {
                var probe = await _backends.ProbeAsync(input.Implementation, settings);
                return new InstanceTestResult(probe.Error, probe.QualityProfiles.Select(p => p.Name).ToList(), probe.RootFolders);
            }
            catch (Exception ex)
            {
                return new InstanceTestResult($"Couldn't reach {input.Implementation} at {input.Url}: {ex.Message}", Array.Empty<string>(), Array.Empty<string>());
            }
        }

        public async Task<InstanceSaveResult> SaveAsync(int? id, InstanceInput input, Actor actor)
        {
            var existing = id is { } saved ? _backends.Find(saved) : null;

            if (Problem(id, existing, input) is { } problem)
            {
                return new InstanceSaveResult(null, problem);
            }

            var instance = existing ?? new BackendInstance { Added = DateTime.UtcNow };
            var before = existing == null ? null : Described(existing);

            instance.Name = input.Name.Trim();
            instance.Implementation = input.Implementation;
            instance.Enable = input.Enabled;
            instance.Settings = Settings(input, existing?.Settings);
            _repository.Upsert(instance);

            if (input.IsDefault)
            {
                ClearOtherDefaults(instance);
            }

            Record(actor, instance, before);
            await _backends.CheckAllAsync();

            return new InstanceSaveResult(View(_backends.Find(instance.Id)), null);
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
                return "Choose Sonarr or Radarr; a saved instance keeps its type.";
            }

            if (string.IsNullOrWhiteSpace(input.Name) || (_backends.FindByName(input.Name.Trim()) is { } named && named.Id != id))
            {
                return "Give the instance a name no other instance has.";
            }

            if (!AppSettingsService.IsWebAddress(input.Url?.Trim()))
            {
                return "The address starts with http:// or https://, for example http://sonarr:8989.";
            }

            return existing == null && string.IsNullOrWhiteSpace(input.ApiKey) ? $"Paste the API key from {input.Implementation}'s Settings, General." : null;
        }

        private void ClearOtherDefaults(BackendInstance instance)
        {
            foreach (var other in _backends.All().Where(b => b.Id != instance.Id && b.Implementation == instance.Implementation && b.Settings.IsDefault))
            {
                other.Settings.IsDefault = false;
                _repository.Update(other);
            }
        }

        private void Record(Actor actor, BackendInstance instance, IReadOnlyList<(string Label, string Value)> before)
        {
            var after = Described(instance);

            if (before == null)
            {
                _audit.Record(actor, AddAction, "instance", instance.Id.ToString(), $"{instance.Name}: {instance.Implementation} at {instance.Settings.Url}");
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
            var settings = instance.Settings;

            return new List<(string, string)>
            {
                ("name", instance.Name),
                ("address", settings.Url),
                (ApiKeyLabel, settings.ApiKey),
                ("quality profile", Shown(settings.QualityProfile)),
                ("root folder", Shown(settings.RootFolder)),
                ("series type", Shown(settings.SeriesType)),
                ("default", OnOff(settings.IsDefault)),
                ("turned on", OnOff(instance.Enable)),
                ("polling only", OnOff(settings.PollingOnly)),
                ("anime sub or dub", OnOff(settings.AnimeEnabled)),
                ("anime priority", settings.AnimePriority),
                ("dub terms", $"{Terms(settings.DubRequired)} / {Terms(settings.DubIgnored)}"),
                ("sub terms", $"{Terms(settings.SubRequired)} / {Terms(settings.SubIgnored)}")
            };
        }

        private static BackendSettings Settings(InstanceInput input, BackendSettings saved)
        {
            var sonarr = input.Implementation == BackendImplementations.Sonarr;

            return new BackendSettings
            {
                Url = input.Url?.Trim(),
                ApiKey = string.IsNullOrWhiteSpace(input.ApiKey) ? saved?.ApiKey : input.ApiKey.Trim(),
                QualityProfile = Blank(input.QualityProfile),
                RootFolder = Blank(input.RootFolder),
                SeriesType = sonarr ? Blank(input.SeriesType) : null,
                IsDefault = input.IsDefault,
                PollingOnly = input.PollingOnly,
                AnimeEnabled = !sonarr || input.AnimeEnabled,
                AnimePriority = AnimeDefaults.PriorityKey(AnimeDefaults.ParsePriority(input.AnimePriority)),
                DubRequired = TermList(input.DubRequired),
                DubIgnored = TermList(input.DubIgnored),
                SubRequired = TermList(input.SubRequired),
                SubIgnored = TermList(input.SubIgnored)
            };
        }

        private InstanceView View(BackendInstance instance)
        {
            var settings = instance.Settings;
            var check = _backends.LastResults.FirstOrDefault(r => r.BackendId == instance.Id);
            var input = new InstanceInput(
                instance.Implementation,
                instance.Name,
                settings.Url,
                null,
                settings.QualityProfile,
                settings.RootFolder,
                settings.SeriesType,
                settings.IsDefault,
                instance.Enable,
                settings.PollingOnly,
                settings.AnimeEnabled,
                settings.AnimePriority,
                Terms(settings.DubRequired),
                Terms(settings.DubIgnored),
                Terms(settings.SubRequired),
                Terms(settings.SubIgnored));

            return new InstanceView(instance.Id, input, !string.IsNullOrEmpty(settings.ApiKey), check?.Ok == true, check?.Error);
        }

        private static string Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Shown(string value) => value ?? "none";

        private static string OnOff(bool value) => value ? "on" : "off";

        private static string Terms(List<string> terms) => terms == null ? string.Empty : string.Join(", ", terms);

        private static List<string> TermList(string terms)
        {
            var list = (terms ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            return list.Any() ? list : null;
        }
    }
}
