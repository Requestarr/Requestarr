using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Settings;

namespace Requestarr.Http.Settings
{
    // Requestarr's own settings and its Sonarr and Radarr instances, for the web app's settings pages and first-run
    // setup. Changes are refused with 409 on a server that has a provisioning file (dev and QA). Needs the API key or a
    // signed-in admin, like the rest of the API.
    public class RequestarrSettingsController : Controller
    {
        private readonly IAppSettingsService _settings;
        private readonly IInstanceSettingsService _instances;
        private readonly ISetupService _setup;

        public RequestarrSettingsController(IAppSettingsService settings, IInstanceSettingsService instances, ISetupService setup)
        {
            _settings = settings;
            _instances = instances;
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

        [HttpGet("/api/v1/requestarr/instances")]
        [Produces("application/json")]
        public IActionResult Instances() => Json(_instances.All());

        [HttpPost("/api/v1/requestarr/instances/test")]
        [Produces("application/json")]
        public async Task<IActionResult> Test([FromBody] InstanceInput input, [FromQuery] int? id) => Json(await _instances.TestAsync(id, input));

        [HttpPost("/api/v1/requestarr/instances")]
        [Produces("application/json")]
        public Task<IActionResult> Add([FromBody] InstanceInput input) => Save(null, input);

        [HttpPut("/api/v1/requestarr/instances/{id:int}")]
        [Produces("application/json")]
        public Task<IActionResult> Change(int id, [FromBody] InstanceInput input) => Save(id, input);

        private async Task<IActionResult> Save(int? id, InstanceInput input)
        {
            var result = await _instances.SaveAsync(id, input, await WebAdmin.ActorAsync(HttpContext));
            return result.Problem == null ? Json(result.Instance) : Refused(result.Problem);
        }

        private ObjectResult Refused(string problem) => Conflict(new { message = problem });

        private static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }
}
