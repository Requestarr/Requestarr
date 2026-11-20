using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Localization;
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
        private readonly PortalSorts _sorts;

        public PortalController(IPortalQueries portal, IPortalRecords records, IPortalAuditLog auditLog, ILocalizationService localization)
        {
            _portal = portal;
            _records = records;
            _auditLog = auditLog;
            _sorts = new PortalSorts(localization.GetLocalizedString);
        }

        [HttpGet("/api/v1/portal/requests")]
        [Produces("application/json")]
        public IActionResult Requests() => Json(_portal.Requests());

        // sections=false leaves out its timeline and followers, which the page reads a page at a time.
        [HttpGet("/api/v1/portal/requests/{id:int}")]
        [Produces("application/json")]
        public IActionResult RequestDetail(int id, [FromQuery] bool sections = true)
        {
            var request = _portal.Request(id);

            if (request == null)
            {
                return NotFound();
            }

            return Json(sections ? request : request with { Timeline = new List<PortalEvent>(), Followers = new List<PortalFollower>() });
        }

        // With page: a page of the members with that status (all with none), as PortalPagesController answers.
        [HttpGet("/api/v1/portal/members")]
        [Produces("application/json")]
        public IActionResult Members([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string sortKey, [FromQuery] string sortDirection, [FromQuery] string status)
        {
            if (page == null)
            {
                return Json(_portal.Members());
            }

            var shown = _portal.Members().Where(m => string.IsNullOrEmpty(status) || string.Equals(m.Status.ToString(), status, StringComparison.OrdinalIgnoreCase));

            return Json(PortalPaging.Of(shown, PortalPagesController.Asked(page, pageSize, sortKey, sortDirection), _sorts.Members));
        }

        // sections=false leaves out their requests, follows, removals and history, which the page reads a page at a time.
        [HttpGet("/api/v1/portal/members/{id:int}")]
        [Produces("application/json")]
        public IActionResult MemberDetail(int id, [FromQuery] bool sections = true)
        {
            var member = _portal.Member(id);

            if (member == null)
            {
                return NotFound();
            }

            return Json(sections ? member : member with { Requests = new List<PortalRequest>(), Following = new List<PortalRequest>(), Removals = new List<PortalRemoval>(), History = new List<PortalEvent>() });
        }

        [HttpGet("/api/v1/portal/queue")]
        [Produces("application/json")]
        public IActionResult Queue() => Json(_portal.Queue());

        [HttpGet("/api/v1/portal/title-removals")]
        [Produces("application/json")]
        public IActionResult TitleRemovals() => Json(_portal.TitleRemovals());

        [HttpGet("/api/v1/portal/access-requests")]
        [Produces("application/json")]
        public IActionResult AccessRequests() => Json(_records.AccessRequests());

        // history=false leaves out past outages, which the page reads a page at a time.
        [HttpGet("/api/v1/portal/outages")]
        [Produces("application/json")]
        public IActionResult Outages([FromQuery] bool history = true) =>
            Json(history ? _records.Outages() : _records.Outages() with { History = new List<PortalOutage>() });

        [HttpGet("/api/v1/portal/types")]
        [Produces("application/json")]
        public IActionResult Types() => Json(_records.Types());

        [HttpGet("/api/v1/portal/changes")]
        [Produces("application/json")]
        public IActionResult Changes() => Json(_records.Changes());

        [HttpGet("/api/v1/portal/instances")]
        [Produces("application/json")]
        public IActionResult Instances() => Json(_records.Instances());

        // Every filter is optional: member, title and action are words they contain; from and to are days. With page: a
        // page of what the filters find, newest first.
        [HttpGet("/api/v1/portal/audit")]
        [Produces("application/json")]
        public IActionResult Audit(
            [FromQuery] string member,
            [FromQuery] string title,
            [FromQuery] string action,
            [FromQuery] DateTime? from,
            [FromQuery] DateTime? to,
            [FromQuery] int? page,
            [FromQuery] int? pageSize)
        {
            var found = _auditLog.Find(new PortalAuditFilter(member, title, action, from, to));

            return page == null
                ? Json(found.Take(PortalAuditLog.PageSize).ToList())
                : Json(PortalPaging.Of(found, PortalPagesController.Asked(page, pageSize, null, null), PortalSorts.None<PortalAuditEntry>()));
        }

        [HttpGet("/api/v1/portal/search")]
        [Produces("application/json")]
        public IActionResult Search([FromQuery] string term) => Json(_auditLog.Search(term));
    }
}
