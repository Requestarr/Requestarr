using NzbDrone.Core.Configuration;
using Requestarr.Core.Availability;

namespace Requestarr.Core.Requests
{
    public interface IPortalLinks
    {
        // The request's page in the web app; null while no address a browser can open is known.
        string Request(int id);
    }

    // The web app as a browser opens it: Settings, General's Application URL, else the address the media managers reach
    // Requestarr at, unless that's a bare container name.
    public class PortalLinks : IPortalLinks
    {
        private readonly IConfigService _config;
        private readonly IAvailabilitySettingsStore _availability;

        public PortalLinks(IConfigService config, IAvailabilitySettingsStore availability)
        {
            _config = config;
            _availability = availability;
        }

        public string Request(int id) =>
            TitleLinks.BrowserRoot(_config.ApplicationUrl, _availability.WebhookUrl) is { } root ? $"{root}/requests/{id}" : null;
    }
}
