using System.Linq;
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
    }

    public class MediaManagerResourceMapper : ProviderResourceMapper<MediaManagerResource, BackendInstance>
    {
        private readonly IBackendService _backends;

        public MediaManagerResourceMapper(IBackendService backends)
        {
            _backends = backends;
        }

        public override MediaManagerResource ToResource(BackendInstance definition)
        {
            var resource = base.ToResource(definition);
            var check = _backends.LastResults.FirstOrDefault(r => r.BackendId == definition.Id);

            resource.Enable = definition.Enable;
            resource.Connected = check?.Ok == true;
            resource.Problem = check?.Error;

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
