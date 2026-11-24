using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Audit;
using Requestarr.Core.Imports;
using Requestarr.Core.Portal;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Http.Portal
{
    public record ReasonBody(string Reason);

    // All: every regular season now and new ones as they air; otherwise exactly Seasons, plus new ones when Future.
    public record SeasonsBody(List<int> Seasons, bool All, bool Future);

    // Seasons empty: the whole title.
    public record RemovalBody(List<int> Seasons, bool Force);

    // Add member: the people picked and what each of them gets.
    // PostHistory: their imported requests go out in their new channels.
    public record OnboardBody(List<OnboardPerson> People, List<OnboardAccess> Access, bool PostHistory = true);

    public record AccessBody(ContentType ContentType, AccessState State);

    // Limit 0: no limit.
    public record LimitBody(ContentType ContentType, int Limit, int Days);

    public record PlexBody(string Username);

    public record AccessLevel(ContentType ContentType, AccessState State);

    public record ApproveAccessBody(List<AccessLevel> Levels);

    // What an admin does in the portal (docs/admin-portal.md, 5c), through the services Discord's buttons use. Each
    // answers { message } in the words Discord would: 200 when it was done, 409 when the service refused it. Needs the
    // API key or a signed-in admin, like the rest of the API.
    public class PortalActionsController : Controller
    {
        private readonly IPortalActions _actions;
        private readonly IPortalMemberActions _members;
        private readonly IPortalOnboarding _onboarding;

        public PortalActionsController(IPortalActions actions, IPortalMemberActions members, IPortalOnboarding onboarding)
        {
            _actions = actions;
            _members = members;
            _onboarding = onboarding;
        }

        [HttpPost("/api/v1/portal/requests/{id:int}/approve")]
        public Task<IActionResult> Approve(int id) => Act(admin => _actions.ApproveAsync(id, admin));

        [HttpPost("/api/v1/portal/requests/{id:int}/deny")]
        public Task<IActionResult> Deny(int id, [FromBody] ReasonBody body) => Act(admin => _actions.DenyAsync(id, body?.Reason, admin));

        [HttpPost("/api/v1/portal/requests/{id:int}/cancel")]
        public Task<IActionResult> Cancel(int id, [FromBody] ReasonBody body) => Act(admin => _actions.CancelAsync(id, body?.Reason, admin));

        [HttpPost("/api/v1/portal/requests/{id:int}/available")]
        public Task<IActionResult> Available(int id) => Act(admin => _actions.MarkAvailableAsync(id, admin));

        [HttpPost("/api/v1/portal/requests/{id:int}/search-again")]
        public Task<IActionResult> SearchAgain(int id) => Act(admin => _actions.SearchAgainAsync(id, admin));

        [HttpGet("/api/v1/portal/removals/deletes-files")]
        public IActionResult DeletesFiles() => Json(new { _actions.DeletesFiles });

        [HttpPost("/api/v1/portal/requests/{id:int}/retry")]
        public Task<IActionResult> Retry(int id) => Act(admin => _actions.RetryAsync(id, admin));

        [HttpPost("/api/v1/portal/requests/{id:int}/seasons")]
        public Task<IActionResult> AddSeasons(int id, [FromBody] SeasonsBody body) =>
            body == null ? Missing() : Act(admin => _actions.AddSeasonsAsync(id, new SeasonChoice(body.All, body.Seasons ?? new List<int>(), body.Future), admin));

        [HttpPost("/api/v1/portal/requests/{id:int}/removal")]
        public Task<IActionResult> StartRemoval(int id, [FromBody] RemovalBody body) =>
            body == null ? Missing() : Act(admin => _actions.StartRemovalAsync(id, body.Seasons, body.Force, admin));

        [HttpGet("/api/v1/portal/library")]
        [Produces("application/json")]
        public async Task<IActionResult> Library([FromQuery] string term) => Json(await _actions.SearchUnrequestedAsync(term));

        [HttpPost("/api/v1/portal/library/{instanceId:int}/{itemId:int}/removal")]
        public Task<IActionResult> StartLibraryRemoval(int instanceId, int itemId, [FromBody] RemovalBody body) =>
            body == null ? Missing() : Act(admin => _actions.StartLibraryRemovalAsync(instanceId, itemId, body.Seasons, body.Force, admin));

        [HttpPost("/api/v1/portal/removals/{id:int}/force")]
        public Task<IActionResult> ForceRemoval(int id) => Act(admin => _actions.ForceRemovalAsync(id, admin));

        [HttpPost("/api/v1/portal/removals/{id:int}/drop")]
        public Task<IActionResult> DropRemoval(int id) => Act(admin => _actions.DropRemovalAsync(id, admin));

        [HttpPost("/api/v1/portal/removals/{id:int}/deleted-by-hand")]
        public Task<IActionResult> DeletedByHand(int id) => Act(admin => _actions.DeletedByHandAsync(id, admin));

        [HttpPost("/api/v1/portal/instances/{id:int}/check")]
        public Task<IActionResult> CheckInstance(int id) => Act(_ => _actions.CheckInstanceAsync(id));

        [HttpPost("/api/v1/portal/instances/{id:int}/announce")]
        public Task<IActionResult> Announce(int id) => Act(admin => _actions.AnnounceOutageAsync(id, admin));

        [HttpGet("/api/v1/portal/server-members")]
        [Produces("application/json")]
        public async Task<IActionResult> ServerMembers() =>
            await _members.ServerMembersAsync() is { } people ? Json(people) : Conflict(new { message = "The Discord bot isn't connected, so the server's people can't be listed." });

        // Starts onboarding everyone picked; the answer is the run, read again at its own address until it's done.
        [HttpPost("/api/v1/portal/members/onboard")]
        public async Task<IActionResult> Onboard([FromBody] OnboardBody body)
        {
            if (body == null)
            {
                return await Missing();
            }

            if (body.People == null || body.People.Count == 0)
            {
                return BadRequest(new { message = "Pick someone from the server." });
            }

            var access = (body.Access ?? new List<OnboardAccess>()).GroupBy(a => a.ContentType).Select(g => g.Last()).ToList();

            var history = body.PostHistory ? OnboardHistory.Post : OnboardHistory.Leave;

            return Json(_onboarding.Start(body.People, access, history, await WebAdmin.ActorAsync(HttpContext)));
        }

        [HttpGet("/api/v1/portal/members/onboard/{id}")]
        [Produces("application/json")]
        public IActionResult Onboarding(string id) => _onboarding.Find(id) is { } run ? Json(run) : NotFound();

        [HttpPut("/api/v1/portal/members/{id:int}/access")]
        public Task<IActionResult> SetAccess(int id, [FromBody] AccessBody body) =>
            body == null ? Missing() : Act(admin => Task.FromResult(_members.SetAccess(id, body.ContentType, body.State, admin)));

        [HttpPut("/api/v1/portal/members/{id:int}/limit")]
        public Task<IActionResult> SetLimit(int id, [FromBody] LimitBody body) =>
            body == null ? Missing() : Act(admin => Task.FromResult(_members.SetLimit(id, body.ContentType, body.Limit, body.Days, admin)));

        [HttpPost("/api/v1/portal/members/{id:int}/suspend")]
        public Task<IActionResult> Suspend(int id) => Act(admin => _members.SuspendAsync(id, admin));

        [HttpPost("/api/v1/portal/members/{id:int}/unsuspend")]
        public Task<IActionResult> Unsuspend(int id) => Act(admin => _members.UnsuspendAsync(id, admin));

        [HttpPost("/api/v1/portal/members/{id:int}/remove")]
        public Task<IActionResult> Remove(int id) => Act(admin => _members.RemoveAsync(id, admin));

        [HttpPost("/api/v1/portal/members/{id:int}/plex")]
        public Task<IActionResult> LinkPlex(int id, [FromBody] PlexBody body) => Act(admin => Task.FromResult(_members.LinkPlex(id, body?.Username, admin)));

        [HttpPost("/api/v1/portal/members/{id:int}/post-history")]
        public Task<IActionResult> PostHistory(int id) => Act(admin => Task.FromResult(_members.PostHistory(id, admin)));

        [HttpPost("/api/v1/portal/media-links/{id:int}/approve")]
        public Task<IActionResult> ApproveMediaLink(int id) => Act(admin => _members.DecideMediaLinkAsync(id, true, admin));

        [HttpPost("/api/v1/portal/media-links/{id:int}/deny")]
        public Task<IActionResult> DenyMediaLink(int id) => Act(admin => _members.DecideMediaLinkAsync(id, false, admin));

        [HttpPost("/api/v1/portal/access-requests/{id:int}/approve")]
        public Task<IActionResult> ApproveAccess(int id, [FromBody] ApproveAccessBody body) =>
            Act(admin => _members.ApproveAccessAsync(id, (body?.Levels ?? new List<AccessLevel>()).ToDictionary(l => l.ContentType, l => l.State), admin));

        [HttpPost("/api/v1/portal/access-requests/{id:int}/deny")]
        public Task<IActionResult> DenyAccess(int id, [FromBody] ReasonBody body) => Act(admin => _members.DenyAccessAsync(id, body?.Reason, admin));

        [HttpPost("/api/v1/portal/paused/{discordId}/unblock")]
        public Task<IActionResult> Unblock(string discordId) => Act(admin => _members.UnblockAsync(discordId, admin));

        // The body didn't read as one: a number field left empty, a value Requestarr doesn't know.
        private Task<IActionResult> Missing() => Task.FromResult<IActionResult>(Conflict(new { message = "Fill in every field with a number or a choice from its list." }));

        private async Task<IActionResult> Act(System.Func<Actor, Task<ServiceResult>> action)
        {
            var result = await action(await WebAdmin.ActorAsync(HttpContext));
            var answer = new { message = result.Message };

            return result.Success ? Json(answer) : Conflict(answer);
        }
    }
}
