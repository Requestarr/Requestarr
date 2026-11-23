using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.SeriesFixes;

namespace Requestarr.Http.Settings
{
    // The web app's Series fixes: the ready list of fixes for shows Sonarr grabs wrong. A save or delete it refuses
    // answers 409 with { message } in plain words, as the portal's actions do.
    public class SeriesFixesController : Controller
    {
        private readonly ISeriesFixList _fixes;

        public SeriesFixesController(ISeriesFixList fixes)
        {
            _fixes = fixes;
        }

        [HttpGet("/api/v1/requestarr/seriesfixes")]
        [Produces("application/json")]
        public IActionResult List() => Json(_fixes.All());

        [HttpPost("/api/v1/requestarr/seriesfixes")]
        public Task<IActionResult> Add([FromBody] SeriesFixInput body) => Answer(async () => _fixes.Save(null, body, await WebAdmin.ActorAsync(HttpContext)));

        [HttpPut("/api/v1/requestarr/seriesfixes/{id:int}")]
        public Task<IActionResult> Change(int id, [FromBody] SeriesFixInput body) => Answer(async () => _fixes.Save(id, body, await WebAdmin.ActorAsync(HttpContext)));

        [HttpDelete("/api/v1/requestarr/seriesfixes/{id:int}")]
        public Task<IActionResult> Remove(int id) => Answer(async () => _fixes.Delete(id, await WebAdmin.ActorAsync(HttpContext)));

        private async Task<IActionResult> Answer(System.Func<Task<SeriesFixResult>> action)
        {
            var result = await action();

            return result.Problem == null ? Json(result.Fix) : Conflict(new { message = result.Problem });
        }
    }
}
