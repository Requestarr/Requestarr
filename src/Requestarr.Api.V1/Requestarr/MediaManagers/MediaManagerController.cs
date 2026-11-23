using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Backends.MediaManagers;
using Requestarr.Core.Settings;
using Requestarr.Http;
using Requestarr.Http.REST;

namespace Requestarr.Api.V1.MediaManagers
{
    // The Sonarr and Radarr instances on Servarr's provider handlers, as Prowlarr's ApplicationController: list, the
    // schema to add one from, add, change, test, test all, delete, and the choices its fields ask for. Deleting keeps
    // the instance's history (InstanceDeletion); there's no bulk delete, since each asks what takes over.
    [V1ApiController("mediamanager")]
    public class MediaManagerController : ProviderControllerBase<MediaManagerResource, MediaManagerBulkResource, IMediaManager, BackendInstance>,
        IHandle<BackendsCheckedEvent>,
        IHandle<AvailabilityWebhooksCheckedEvent>
    {
        private readonly IInstanceDeletion _deletion;

        public MediaManagerController(IBroadcastSignalRMessage signalRBroadcaster,
                                      IMediaManagerFactory factory,
                                      MediaManagerResourceMapper resourceMapper,
                                      MediaManagerBulkResourceMapper bulkResourceMapper,
                                      IInstanceDeletion deletion)
            : base(signalRBroadcaster, factory, "mediamanager", resourceMapper, bulkResourceMapper)
        {
            _deletion = deletion;
        }

        // The signed-in admin, for the change record an add or change writes.
        [NonAction]
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            ActingAdmin.Current = await WebAdmin.ActorAsync(HttpContext);
            await base.OnActionExecutionAsync(context, next);
        }

        // What deleting it would do, for its confirm.
        [HttpGet("{id:int}/deletion")]
        [Produces("application/json")]
        public ActionResult<InstanceDeletionPreview> Deletion(int id) => _deletion.Preview(id) is { } preview ? preview : NotFound();

        // takeOver: the instance that becomes its type's main one, when this one was.
        public override object DeleteProvider(int id)
        {
            var takeOver = int.TryParse(Request.Query["takeOver"], out var heir) ? heir : (int?)null;
            var result = _deletion.DeleteAsync(id, takeOver, ActingAdmin.Current).GetAwaiter().GetResult();

            if (!result.Deleted)
            {
                throw new BadRequestException(result.Problem);
            }

            BroadcastResourceChange(ModelAction.Deleted, id);

            return result;
        }

        public override object DeleteProviders([FromBody] MediaManagerBulkResource resource) => throw new BadRequestException("Delete each instance on its own.");

        // Tags mean nothing to an instance: one is changed on its own, through the settings rules.
        public override ActionResult<MediaManagerResource> UpdateProvider([FromBody] MediaManagerBulkResource providerResource) =>
            throw new BadRequestException("Change each instance on its own.");

        // A connection check finished: the page shows each instance's new state.
        [NonAction]
        public void Handle(BackendsCheckedEvent message) => BroadcastResourceChange(ModelAction.Sync);

        // Their webhooks were tested: each card's Updates line follows (right away, or on the timer and why).
        [NonAction]
        public void Handle(AvailabilityWebhooksCheckedEvent message) => BroadcastResourceChange(ModelAction.Sync);
    }
}
