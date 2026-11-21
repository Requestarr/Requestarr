using Requestarr.Core.Settings;

namespace Requestarr.Core.Backends.MediaManagers
{
    // Sonarr: requests for shows and seasons go here.
    public class Sonarr : MediaManagerBase
    {
        public Sonarr(IInstanceSettingsService instances)
            : base(instances)
        {
        }

        protected override string DefaultUrl => "http://localhost:8989";
    }
}
