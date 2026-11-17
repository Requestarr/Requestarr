using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Portal;

namespace Requestarr.Http.Portal
{
    // The admin portal's read views (docs/admin-portal.md): every request with its requester and followers, every
    // member with their access and limits, and what's waiting on an admin. Needs the API key or a signed-in admin,
    // like the rest of the API. Acting on these comes later (task 5c).
    public class PortalController : Controller
    {
        private readonly IPortalQueries _portal;

        public PortalController(IPortalQueries portal)
        {
            _portal = portal;
        }

        [HttpGet("/api/v1/portal/requests")]
        [Produces("application/json")]
        public IActionResult Requests() => Json(_portal.Requests());

        [HttpGet("/api/v1/portal/requests/{id:int}")]
        [Produces("application/json")]
        public IActionResult RequestDetail(int id) => _portal.Request(id) is { } request ? Json(request) : NotFound();

        [HttpGet("/api/v1/portal/members")]
        [Produces("application/json")]
        public IActionResult Members() => Json(_portal.Members());

        [HttpGet("/api/v1/portal/members/{id:int}")]
        [Produces("application/json")]
        public IActionResult MemberDetail(int id) => _portal.Member(id) is { } member ? Json(member) : NotFound();

        [HttpGet("/api/v1/portal/queue")]
        [Produces("application/json")]
        public IActionResult Queue() => Json(_portal.Queue());
    }
}
