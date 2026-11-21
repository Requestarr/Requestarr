using Requestarr.Core.Settings;

namespace Requestarr.Core.Backends.MediaManagers
{
    // Radarr: requests for films go here.
    public class Radarr : MediaManagerBase
    {
        public Radarr(IInstanceSettingsService instances)
            : base(instances)
        {
        }

        protected override string DefaultUrl => "http://localhost:7878";
    }
}
