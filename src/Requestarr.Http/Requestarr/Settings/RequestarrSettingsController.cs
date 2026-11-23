using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Availability;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
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
        private readonly IDiscordClientHost _discord;
        private readonly IDiscordAdmins _admins;
        private readonly IAvailabilityService _availability;

        public RequestarrSettingsController(IAppSettingsService settings, ISetupService setup, IDiscordClientHost discord, IDiscordAdmins admins, IAvailabilityService availability)
        {
            _settings = settings;
            _setup = setup;
            _discord = discord;
            _admins = admins;
            _availability = availability;
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

        // { "url": "http://..." }: each Sonarr and Radarr that's turned on tries its own Connect test of this address for
        // Requestarr, saving nothing.
        [HttpPost("/api/v1/requestarr/settings/webhook-test")]
        [Produces("application/json")]
        public async Task<IActionResult> TestWebhookAddress([FromBody] AddressChoice choice)
        {
            if (!AppSettingsService.IsWebAddress(choice?.Url?.Trim()))
            {
                return Refused("The address starts with http:// or https://, for example http://192.168.1.10:9797.");
            }

            return Json(await _availability.TestAddressAsync(choice.Url.Trim()));
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

        // The bot is in several servers: the admin picks which one is Requestarr's.
        [HttpPost("/api/v1/requestarr/setup/discord/server")]
        [Produces("application/json")]
        public async Task<IActionResult> ChooseServer([FromBody] ServerChoice choice)
        {
            if (!ulong.TryParse(choice?.Id, out var id) || !await _discord.ChooseServerAsync(id, await WebAdmin.ActorAsync(HttpContext)))
            {
                return Refused("The bot isn't in that server, or this server's provisioning file says which server it is.");
            }

            return Json(_setup.Status());
        }

        // Everyone in Requestarr's server, with who holds the admin role.
        [HttpGet("/api/v1/requestarr/discord/admins")]
        [Produces("application/json")]
        public async Task<IActionResult> Admins() => Json(await _admins.ListAsync());

        // { "ids": ["..."] }: exactly these people hold the admin role, and the server's owner always does.
        [HttpPut("/api/v1/requestarr/discord/admins")]
        [Produces("application/json")]
        public async Task<IActionResult> SaveAdmins([FromBody] AdminChoice choice)
        {
            var ids = (choice?.Ids ?? new List<string>()).Select(i => ulong.TryParse(i, out var id) ? id : 0).Where(id => id != 0).ToList();
            var result = await _admins.SetAsync(ids, await WebAdmin.ActorAsync(HttpContext));

            return result.Saved ? Json(await _admins.ListAsync()) : Refused(result.Problem);
        }

        // The bot leaves a server it only read (an import from Requestrr is done); never Requestarr's own.
        [HttpPost("/api/v1/requestarr/discord/servers/{id}/leave")]
        [Produces("application/json")]
        public async Task<IActionResult> LeaveServer(string id)
        {
            if (!ulong.TryParse(id, out var guildId) || !await _discord.LeaveServerAsync(guildId))
            {
                return Refused("The bot isn't in that server, or it's Requestarr's own server.");
            }

            return Json(_setup.Status());
        }

        private ObjectResult Refused(string problem) => Conflict(new { message = problem });

        public record ServerChoice(string Id);

        public record AdminChoice(List<string> Ids);

        public record AddressChoice(string Url);

        internal static string Text(JsonElement value) => value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
            _ => value.GetRawText()
        };
    }
}
