using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Audit;
using Requestarr.Core.Portal;
using Requestarr.Core.Users;

namespace Requestarr.Http.Portal
{
    // The requests picked and what to do to them: approve, deny (with the reason) or start a removal (Seasons empty:
    // the whole title; Force: no vote).
    public record BulkRequestsBody(List<int> Ids, string Action, string Reason, List<int> Seasons, bool Force);

    // The members picked and what to do to them: suspend, unsuspend, remove-member, or changeAccess (each change in
    // Changes, the member's own instance left as it is).
    public record BulkMembersBody(List<int> Ids, string Action, List<AccessBody> Changes);

    public record BulkFailure(int Id, string Message);

    // How many of the picks it did, and why each of the others was refused.
    public record BulkAnswer(int Done, int Total, List<BulkFailure> Failed);

    // Actions on many picks in one call, as Servarr's bulk edits act on every item picked (ProviderBulkResource, the
    // series editor): each through the same service its own button uses, one after another, a refusal not stopping
    // the rest. Needs the API key or a signed-in admin, like the rest of the API.
    public class PortalBulkController : Controller
    {
        private const string UnknownAction = "Requestarr doesn't know that action.";

        private readonly IPortalActions _actions;
        private readonly IPortalMemberActions _members;

        public PortalBulkController(IPortalActions actions, IPortalMemberActions members)
        {
            _actions = actions;
            _members = members;
        }

        [HttpPost("/api/v1/portal/requests/bulk")]
        [Produces("application/json")]
        public async Task<IActionResult> Requests([FromBody] BulkRequestsBody body) =>
            RequestAction(body) is { } act ? Json(await EachAsync(body.Ids, act)) : BadRequest(new { message = UnknownAction });

        [HttpPost("/api/v1/portal/members/bulk")]
        [Produces("application/json")]
        public async Task<IActionResult> Members([FromBody] BulkMembersBody body) =>
            MemberAction(body) is { } act ? Json(await EachAsync(body.Ids, act)) : BadRequest(new { message = UnknownAction });

        private Func<int, Actor, Task<ServiceResult>> RequestAction(BulkRequestsBody body) => body?.Action switch
        {
            "approve" => (id, admin) => _actions.ApproveAsync(id, admin),
            "deny" => (id, admin) => _actions.DenyAsync(id, body.Reason, admin),
            "remove" => (id, admin) => _actions.StartRemovalAsync(id, body.Seasons ?? new List<int>(), body.Force, admin),
            _ => null
        };

        private Func<int, Actor, Task<ServiceResult>> MemberAction(BulkMembersBody body) => body?.Action switch
        {
            "suspend" => (id, admin) => _members.SuspendAsync(id, admin),
            "unsuspend" => (id, admin) => _members.UnsuspendAsync(id, admin),
            "remove-member" => (id, admin) => _members.RemoveAsync(id, admin),
            "changeAccess" => (id, admin) => Task.FromResult(ChangeAccess(id, body.Changes ?? new List<AccessBody>(), admin)),
            _ => null
        };

        // Each type's change in turn; the first refusal is the member's.
        private ServiceResult ChangeAccess(int id, List<AccessBody> changes, Actor admin)
        {
            foreach (var change in changes)
            {
                var result = _members.SetAccess(id, change.ContentType, change.State, null, admin);

                if (!result.Success)
                {
                    return result;
                }
            }

            return ServiceResult.Ok(string.Empty);
        }

        private async Task<BulkAnswer> EachAsync(List<int> ids, Func<int, Actor, Task<ServiceResult>> act)
        {
            var admin = await WebAdmin.ActorAsync(HttpContext);
            var picked = ids ?? new List<int>();
            var failed = new List<BulkFailure>();

            foreach (var id in picked)
            {
                var result = await act(id, admin);

                if (!result.Success)
                {
                    failed.Add(new BulkFailure(id, result.Message));
                }
            }

            return new BulkAnswer(picked.Count - failed.Count, picked.Count, failed);
        }
    }
}
