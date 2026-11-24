using NzbDrone.Core.Configuration;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Imports
{
    // Where imported history goes by itself: in someone's new channel when they're onboarded (off unless an admin turns it
    // on), and in the admin feed (on unless an admin turns it off). Set in the server's window under Connections, or in the
    // provisioning file.
    public interface IHistorySettings
    {
        bool WhenOnboarding { get; }
        bool InFeed { get; }

        void Apply(ImportedHistoryProvisioning provisioning);
    }

    public class HistorySettings : IHistorySettings
    {
        private readonly IConfigService _config;

        public HistorySettings(IConfigService config)
        {
            _config = config;
        }

        public bool WhenOnboarding => _config.OnboardPostHistory;

        public bool InFeed => _config.ImportPostFeed;

        public void Apply(ImportedHistoryProvisioning provisioning)
        {
            if (provisioning?.WhenOnboarding is { } whenOnboarding)
            {
                _config.OnboardPostHistory = whenOnboarding;
            }

            if (provisioning?.InFeed is { } inFeed)
            {
                _config.ImportPostFeed = inFeed;
            }
        }
    }
}
