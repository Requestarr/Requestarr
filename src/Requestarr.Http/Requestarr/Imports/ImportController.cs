using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Imports;

namespace Requestarr.Http.Imports
{
    // Importing past requests from another request bot's channels (Requestrr), for the web app's Import page: the
    // channels the bot can read, a preview of what an import of the ones picked would do, then confirm it. Needs the API
    // key or a signed-in admin, like the rest of the API. Channel IDs travel as text: they're too long for a browser's
    // numbers.
    public class ImportController : Controller
    {
        public const string NotAChannelId = "That isn't a channel ID: it's the long number Discord copies with Developer Mode on (right-click the channel, Copy Channel ID).";

        private readonly IImportService _imports;
        private readonly IHistoryPosting _posting;

        public ImportController(IImportService imports, IHistoryPosting posting)
        {
            _imports = imports;
            _posting = posting;
        }

        // Imported history being posted in Discord: "312 of 467 posted", and Pause, Resume and Cancel.
        [HttpGet("/api/v1/requestarr/import/history")]
        [Produces("application/json")]
        public IActionResult History() => Json(_posting.Status());

        // The whole server's imported requests: what isn't posted yet, never twice.
        [HttpPost("/api/v1/requestarr/import/history/post")]
        public async Task<IActionResult> PostHistory()
        {
            var result = _posting.PostServer(await WebAdmin.ActorAsync(HttpContext));

            return result.Success ? Json(_posting.Status()) : Conflict(new { message = result.Message });
        }

        [HttpPost("/api/v1/requestarr/import/history/pause")]
        public IActionResult Pause() => Answer(_posting.Pause);

        [HttpPost("/api/v1/requestarr/import/history/resume")]
        public IActionResult Resume() => Answer(_posting.Resume);

        [HttpPost("/api/v1/requestarr/import/history/cancel")]
        public async Task<IActionResult> Cancel()
        {
            var admin = await WebAdmin.ActorAsync(HttpContext);
            return Answer(() => _posting.Cancel(admin));
        }

        private IActionResult Answer(Action action)
        {
            action();
            return Json(_posting.Status());
        }

        // The read-only invite link for another server, and whether the bot may read other bots' messages.
        [HttpGet("/api/v1/requestarr/import/access")]
        [Produces("application/json")]
        public async Task<IActionResult> Access() => Json(await _imports.AccessAsync());

        [HttpGet("/api/v1/requestarr/import/channels")]
        [Produces("application/json")]
        public async Task<IActionResult> Channels() => Json(await _imports.ReadableChannelsAsync());

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
