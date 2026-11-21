using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using NzbDrone.Core.Datastore.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.SignalR;
using Requestarr.Core.Backends;
using Requestarr.Core.Backends.MediaManagers;
using Requestarr.Http;
using Requestarr.Http.REST;

namespace Requestarr.Api.V1.MediaManagers
{
    // The Sonarr and Radarr instances on Servarr's provider handlers, as Prowlarr's ApplicationController: list, the
    // schema to add one from, add, change, test, test all, and the choices its fields ask for. Instances are never
    // deleted, since requests keep their history with them: they're turned off instead.
    [V1ApiController("mediamanager")]
    public class MediaManagerController : ProviderControllerBase<MediaManagerResource, MediaManagerBulkResource, IMediaManager, BackendInstance>,
        IHandle<BackendsCheckedEvent>
    {
        private const string NeverDeleted = "An instance can't be deleted, since requests keep their history with it. Turn it off instead.";

        public MediaManagerController(IBroadcastSignalRMessage signalRBroadcaster,
                                      IMediaManagerFactory factory,
                                      MediaManagerResourceMapper resourceMapper,
                                      MediaManagerBulkResourceMapper bulkResourceMapper)
            : base(signalRBroadcaster, factory, "mediamanager", resourceMapper, bulkResourceMapper)
        {
        }

        // The signed-in admin, for the change record an add or change writes.
        [NonAction]
        public override async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
        {
            ActingAdmin.Current = await WebAdmin.ActorAsync(HttpContext);
            await base.OnActionExecutionAsync(context, next);
        }

        public override object DeleteProvider(int id) => throw new BadRequestException(NeverDeleted);

        public override object DeleteProviders([FromBody] MediaManagerBulkResource resource) => throw new BadRequestException(NeverDeleted);

        // Tags mean nothing to an instance: one is changed on its own, through the settings rules.
        public override ActionResult<MediaManagerResource> UpdateProvider([FromBody] MediaManagerBulkResource providerResource) =>
            throw new BadRequestException("Change each instance on its own.");

        // A connection check finished: the page shows each instance's new state.
        [NonAction]
        public void Handle(BackendsCheckedEvent message) => BroadcastResourceChange(ModelAction.Sync);
    }
}
