using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Imports;

namespace Requestarr.Http.Imports
{
    // Importing past requests from another request bot's channels (Requestrr), for the web app's Import page: check a
    // channel, preview what an import would do, then confirm it. Needs the API key or a signed-in admin, like the rest
    // of the API. Channel IDs travel as text: they're too long for a browser's numbers.
    public class ImportController : Controller
    {
        public const string NotAChannelId = "That isn't a channel ID: it's the long number Discord copies with Developer Mode on (right-click the channel, Copy Channel ID).";

        private readonly IImportService _imports;

        public ImportController(IImportService imports)
        {
            _imports = imports;
        }

        [HttpGet("/api/v1/requestarr/import/channels")]
        [Produces("application/json")]
        public async Task<IActionResult> Channels() => Json(await _imports.ReadableChannelsAsync());

        [HttpGet("/api/v1/requestarr/import/channels/{id}")]
        [Produces("application/json")]
        public async Task<IActionResult> Channel(string id) =>
            ChannelId(id) is { } channel ? Json(await _imports.DescribeAsync(channel)) : BadRequest(new { message = NotAChannelId });

        // { "channelIds": ["123…", "456…"] }
        [HttpPost("/api/v1/requestarr/import/preview")]
        [Produces("application/json")]
        public async Task<IActionResult> Preview([FromBody] PreviewBody body)
        {
            var ids = (body?.ChannelIds ?? new List<string>()).Select(ChannelId).ToList();

            return ids.Any(i => i == null) ? BadRequest(new { message = NotAChannelId }) : Json(await _imports.StartPreviewAsync(ids.Select(i => i.Value).ToList()));
        }

        [HttpGet("/api/v1/requestarr/import/preview/{id:guid}")]
        [Produces("application/json")]
        public IActionResult Job(Guid id) => _imports.Job(id) is { } job ? Json(job) : NotFound();

        [HttpPost("/api/v1/requestarr/import/preview/{id:guid}/confirm")]
        [Produces("application/json")]
        public async Task<IActionResult> Confirm(Guid id) =>
            await _imports.ConfirmAsync(id, await WebAdmin.ActorAsync(HttpContext)) is { } job ? Json(job) : NotFound();

        private static ulong? ChannelId(string text) =>
            ulong.TryParse(text?.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

        public class PreviewBody
        {
            public List<string> ChannelIds { get; set; }
        }
    }
}
