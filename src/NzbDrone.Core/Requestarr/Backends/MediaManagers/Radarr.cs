using Requestarr.Core.Settings;

namespace Requestarr.Core.Backends.MediaManagers
{
    // Radarr: requests for films go here.
    public class Radarr : MediaManagerBase
    {
        public Radarr(IInstanceSettingsService instances, IBackendRepository repository)
            : base(instances, repository)
        {
        }
    }
}
