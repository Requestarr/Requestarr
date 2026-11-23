using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Settings
{
    public record InstanceChoice(int Id, string Name);

    // What deleting an instance would do, for its confirm: the requests still waiting on it, whether it's its type's
    // main one (requests of that type go to it), and which instances could take over from it.
    public record InstanceDeletionPreview(string Name, string Implementation, int OpenRequests, bool IsMain, IReadOnlyList<InstanceChoice> TakeOver);

    // WebhookProblem: why Requestarr's Connect entry couldn't be removed from it (it couldn't be reached), or null.
    public record InstanceDeleteResult(bool Deleted, string Problem, string WebhookProblem, int OpenRequests);

    public interface IInstanceDeletion
    {
        InstanceDeletionPreview Preview(int id);

        // TakeOver: the instance that becomes its type's main one, when this one was.
        Task<InstanceDeleteResult> DeleteAsync(int id, int? takeOver, Actor actor);
    }

    // Deletes a Sonarr or Radarr, as Sonarr deletes a provider, but keeps its history: its row stays as deleted (gone
    // from every list and picker), and every request that went to it still shows where it went. Requestarr's Connect
    // entry in it is removed when it can be reached; Requestarr's anime tags and fix profiles stay on the series that use
    // them. Requests still waiting on it are marked as needing a new home, so the admin queue lists them.
    public class InstanceDeletion : IInstanceDeletion
    {
        public const string DeleteAction = "instance.delete";

        private readonly IBackendService _backends;
        private readonly IBackendRepository _repository;
        private readonly IRequestRepository _requests;
        private readonly IProvisioningService _provisioning;
        private readonly IConfigFileProvider _configFile;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public InstanceDeletion(IBackendService backends,
                                IBackendRepository repository,
                                IRequestRepository requests,
                                IProvisioningService provisioning,
                                IConfigFileProvider configFile,
                                IAuditService audit,
                                Logger logger)
        {
            _backends = backends;
            _repository = repository;
            _requests = requests;
            _provisioning = provisioning;
            _configFile = configFile;
            _audit = audit;
            _logger = logger;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public InstanceDeletionPreview Preview(int id)
        {
            var instance = _repository.Find(id);

            if (instance == null || instance.IsDeleted)
            {
                return null;
            }

            var others = Others(instance).Select(o => new InstanceChoice(o.Id, o.Name)).ToList();

            return new InstanceDeletionPreview(instance.Name, instance.Implementation, OpenRequests(instance).Count, IsMain(instance), others);
        }

        public async Task<InstanceDeleteResult> DeleteAsync(int id, int? takeOver, Actor actor)
        {
            var instance = _repository.Find(id);

            if (Problem(instance, takeOver) is { } problem)
            {
                return new InstanceDeleteResult(false, problem, null, 0);
            }

            var heir = takeOver.HasValue ? _repository.Find(takeOver.Value) : null;
            var webhookProblem = await RemoveWebhookAsync(instance);
            var open = OpenRequests(instance);

            if (heir != null && IsMain(instance))
            {
                heir.Config.Purpose = InstancePurpose.Everything;
                _repository.Update(heir);
            }

            foreach (var request in open)
            {
                request.Status = RequestStatus.Failed;
                request.DecisionReason = NeedsNewHome(instance.Name);
                _requests.Update(request);
            }

            var described = Described(instance, heir, open.Count);
            instance.Name = DeletedName(instance);
            instance.Deleted = Clock();
            instance.Enable = false;
            instance.Config.Purpose = InstancePurpose.Nothing;
            instance.DownSince = null;
            _repository.Update(instance);

            _audit.Record(actor, DeleteAction, "instance", instance.Id.ToString(), described);
            await _backends.CheckAllAsync();

            return new InstanceDeleteResult(true, null, webhookProblem, open.Count);
        }

        public static string NeedsNewHome(string name) => $"Needs a new home: {name} was deleted. Retry sends it to its type's main instance.";

        // "Sonarr (deleted)", or "Sonarr (deleted 2)" when an earlier deleted Sonarr has that name already.
        private string DeletedName(BackendInstance instance)
        {
            var taken = _repository.Known().Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var name = $"{instance.Name} (deleted)";

            for (var n = 2; taken.Contains(name); n++)
            {
                name = $"{instance.Name} (deleted {n})";
            }

            return name;
        }

        private string Problem(BackendInstance instance, int? takeOver)
        {
            if (_provisioning.UsesFile)
            {
                return AppSettingsService.ProvisionedProblem;
            }

            if (instance == null || instance.IsDeleted)
            {
                return "That instance no longer exists.";
            }

            return takeOver.HasValue && Others(instance).All(o => o.Id != takeOver.Value) ? "Choose another instance of the same type to take over." : null;
        }

        private async Task<string> RemoveWebhookAsync(BackendInstance instance)
        {
            try
            {
                await _backends.ApiFor(instance).RemoveWebhookAsync(instance.Config, WebhookTokens.For(_configFile.ApiKey, instance.Id));
                return null;
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Couldn't remove Requestarr's webhook from {0}", instance.Name);
                return $"Requestarr couldn't reach {instance.Name} to remove its Connect entry there, so remove \"Requestarr\" under {instance.Implementation}'s Settings, then Connect, yourself.";
            }
        }

        // Requests not finished yet: waiting for approval, or sent and not there yet.
        private List<Request> OpenRequests(BackendInstance instance) =>
            _requests.WithStatus(RequestStatus.Pending).Concat(_requests.WithStatus(RequestStatus.Approved)).Where(r => r.BackendId == instance.Id).ToList();

        private bool IsMain(BackendInstance instance) =>
            _backends.Default(ContentTypeOf(instance)) is { } main && main.Id == instance.Id;

        private List<BackendInstance> Others(BackendInstance instance) =>
            _repository.All().Where(o => o.Id != instance.Id && o.Implementation == instance.Implementation && o.Enable).OrderBy(o => o.Name).ToList();

        private static Users.ContentType ContentTypeOf(BackendInstance instance) =>
            instance.Implementation == BackendImplementations.Radarr ? Users.ContentType.Movie : Users.ContentType.Tv;

        private static string Described(BackendInstance instance, BackendInstance heir, int open)
        {
            var parts = new List<string> { $"{instance.Name}: deleted" };

            if (heir != null)
            {
                parts.Add($"{heir.Name} takes over");
            }

            if (open > 0)
            {
                parts.Add($"{open} open request{(open == 1 ? string.Empty : "s")} need a new home");
            }

            return string.Join("; ", parts);
        }
    }
}
