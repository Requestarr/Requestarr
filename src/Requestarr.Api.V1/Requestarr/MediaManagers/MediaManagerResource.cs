using System.Linq;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;

namespace Requestarr.Api.V1.MediaManagers
{
    // A Sonarr or Radarr instance as the Connections page shows it, as Prowlarr's ApplicationResource: its provider
    // fields, whether it's turned on, and how its last connection check went.
    public class MediaManagerResource : ProviderResource<MediaManagerResource>
    {
        public bool Enable { get; set; }

        // The last connection check: connected, or why not. Neither until the first check.
        public bool Connected { get; set; }
        public string Problem { get; set; }

        // How it tells Requestarr about downloads: right away, or on the timer and why.
        public InstanceUpdates Updates { get; set; }
    }

    public class MediaManagerResourceMapper : ProviderResourceMapper<MediaManagerResource, BackendInstance>
    {
        private readonly IBackendService _backends;
        private readonly IAvailabilityService _availability;

        public MediaManagerResourceMapper(IBackendService backends, IAvailabilityService availability)
        {
            _backends = backends;
            _availability = availability;
        }

        public override MediaManagerResource ToResource(BackendInstance definition)
        {
            var resource = base.ToResource(definition);
            var check = _backends.LastResults.FirstOrDefault(r => r.BackendId == definition.Id);

            resource.Enable = definition.Enable;
            resource.Connected = check?.Ok == true;
            resource.Problem = check?.Error;
            resource.Updates = definition.Id > 0 ? _availability.UpdatesFor(definition) : null;

            return resource;
        }

        public override BackendInstance ToModel(MediaManagerResource resource, BackendInstance existingDefinition)
        {
            var definition = base.ToModel(resource, existingDefinition);

            if (definition != null)
            {
                definition.Enable = resource.Enable;
            }

            return definition;
        }
    }

    public class MediaManagerBulkResource : ProviderBulkResource<MediaManagerBulkResource>
    {
    }

    public class MediaManagerBulkResourceMapper : ProviderBulkResourceMapper<MediaManagerBulkResource, BackendInstance>
    {
    }
}
