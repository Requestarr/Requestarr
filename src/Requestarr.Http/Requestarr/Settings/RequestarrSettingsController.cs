using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Settings;

namespace Requestarr.Http.Settings
{
    // Requestarr's own settings, for the web app's settings pages and first-run setup (the Sonarr and Radarr instances
    // are the media managers' provider API). Changes are refused with 409 on a server that has a provisioning file (dev
    // and QA). Needs the API key or a signed-in admin, like the rest of the API.
    public class RequestarrSettingsController : Controller
    {
        private readonly IAppSettingsService _settings;
        private readonly ISetupService _setup;

        public RequestarrSettingsController(IAppSettingsService settings, ISetupService setup)
        {
            _settings = settings;
            _setup = setup;
        }

        [HttpGet("/api/v1/requestarr/settings")]
        [Produces("application/json")]
        public IActionResult GetSettings() => Json(_settings.Get());

        // Only the settings sent are changed: { "removalVoteDays": 5, "discordBotToken": "..." }.
        [HttpPut("/api/v1/requestarr/settings")]
        [Produces("application/json")]
        public async Task<IActionResult> SaveSettings([FromBody] Dictionary<string, JsonElement> changes)
        {
            var result = await _settings.SaveAsync(changes.ToDictionary(c => c.Key, c => Text(c.Value)), await WebAdmin.ActorAsync(HttpContext));
            return result.Saved ? Json(_settings.Get()) : Refused(result.Problem);
        }

        [HttpGet("/api/v1/requestarr/settings/changes")]
        [Produces("application/json")]
        public IActionResult Changes() => Json(_setup.Changes());

        [HttpGet("/api/v1/requestarr/setup")]
        [Produces("application/json")]
        public IActionResult Setup() => Json(_setup.Status());

        // The first-run setup's Sonarr or Radarr step, left for later: they're added on the Connections page instead.
        [HttpPost("/api/v1/requestarr/setup/skip-instance")]
        [Produces("application/json")]
        public IActionResult SkipInstance() => Json(_setup.SkipInstance());

        private ObjectResult Refused(string problem) => Conflict(new { message = problem });

        internal static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }
}
