using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Settings;

namespace Requestarr.Http.Settings
{
    // Requestarr's own settings as one item, read and saved whole as Servarr's /config/host and /config/ui are, for the
    // web app's settings.requestarr section (Servarr's createFetchHandler and createSaveHandler): every setting by name,
    // a secret as whether one is set, and provisioned when the provisioning file sets them (read-only then). Needs the
    // API key or a signed-in admin, like the rest of the API.
    public class RequestarrConfigController : Controller
    {
        private const string Provisioned = "provisioned";

        private readonly IAppSettingsService _settings;

        public RequestarrConfigController(IAppSettingsService settings)
        {
            _settings = settings;
        }

        [HttpGet("/api/v1/config/requestarr")]
        [Produces("application/json")]
        public IActionResult GetItem() => Json(Item());

        [HttpPut("/api/v1/config/requestarr")]
        [Produces("application/json")]
        public async Task<IActionResult> SaveItem([FromBody] Dictionary<string, JsonElement> item)
        {
            var values = (item ?? new Dictionary<string, JsonElement>()).ToDictionary(i => i.Key, i => RequestarrSettingsController.Text(i.Value));
            var result = await _settings.SaveItemAsync(values, await WebAdmin.ActorAsync(HttpContext));

            return result.Saved ? Json(Item()) : Conflict(new { message = result.Problem });
        }

        private Dictionary<string, object> Item()
        {
            var view = _settings.Get();

            return view.Values.Append(new KeyValuePair<string, object>(Provisioned, view.Provisioned)).ToDictionary(v => v.Key, v => v.Value);
        }
    }
}
