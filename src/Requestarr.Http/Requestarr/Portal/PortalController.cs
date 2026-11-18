using System;
using Microsoft.AspNetCore.Mvc;
using Requestarr.Core.Portal;

namespace Requestarr.Http.Portal
{
    // The admin portal's read views (docs/admin-portal.md): every request with its requester and followers, every
    // member with their access and limits, what's waiting on an admin, people asking to join, outages, the audit log
    // and the header search. Needs the API key or a signed-in admin, like the rest of the API. PortalActionsController
    // acts on them.
    public class PortalController : Controller
    {
        private readonly IPortalQueries _portal;
        private readonly IPortalRecords _records;
        private readonly IPortalAuditLog _auditLog;

        public PortalController(IPortalQueries portal, IPortalRecords records, IPortalAuditLog auditLog)
        {
            _portal = portal;
            _records = records;
            _auditLog = auditLog;
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

        [HttpGet("/api/v1/portal/title-removals")]
        [Produces("application/json")]
        public IActionResult TitleRemovals() => Json(_portal.TitleRemovals());

        [HttpGet("/api/v1/portal/access-requests")]
        [Produces("application/json")]
        public IActionResult AccessRequests() => Json(_records.AccessRequests());

        [HttpGet("/api/v1/portal/outages")]
        [Produces("application/json")]
        public IActionResult Outages() => Json(_records.Outages());

        [HttpGet("/api/v1/portal/changes")]
        [Produces("application/json")]
        public IActionResult Changes() => Json(_records.Changes());

        [HttpGet("/api/v1/portal/instances")]
        [Produces("application/json")]
        public IActionResult Instances() => Json(_records.Instances());

        // Every filter is optional: member, title and action are words they contain; from and to are days.
        [HttpGet("/api/v1/portal/audit")]
        [Produces("application/json")]
        public IActionResult Audit([FromQuery] string member, [FromQuery] string title, [FromQuery] string action, [FromQuery] DateTime? from, [FromQuery] DateTime? to) =>
            Json(_auditLog.Find(new PortalAuditFilter(member, title, action, from, to)));

        [HttpGet("/api/v1/portal/search")]
        [Produces("application/json")]
        public IActionResult Search([FromQuery] string term) => Json(_auditLog.Search(term));
    }
}
