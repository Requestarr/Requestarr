using System;
using Microsoft.AspNetCore.Mvc;
using NzbDrone.Core.Localization;
using Requestarr.Core.Portal;
using Requestarr.Core.Users;

namespace Requestarr.Http.Portal
{
    // The portal's long lists a page at a time (page from 1, pageSize, sortKey, sortDirection ascending or descending),
    // each section of a member's or request's page on its own, so a page never loads every row. Answers
    // { page, pageSize, totalRecords, records }, Servarr's paging format. A section of a member's or request's page is
    // also read with its ID in the query (members/requests?id=5), as the web app's server-side lists send it, and a
    // member's requests with the state or type the page's filter asks for. Needs the API key or a signed-in admin, like
    // the rest of the API.
    public class PortalPagesController : Controller
    {
        private readonly IPortalQueries _portal;
        private readonly IPortalRecords _records;
        private readonly PortalSorts _sorts;

        public PortalPagesController(IPortalQueries portal, IPortalRecords records, ILocalizationService localization)
        {
            _portal = portal;
            _records = records;
            _sorts = new PortalSorts(localization.GetLocalizedString);
        }

        // The page the query asks for; page 1 of the default size when it doesn't say.
        public static PortalPageRequest Asked(int? page, int? pageSize, string sortKey, string sortDirection) =>
            new(page ?? 1, pageSize ?? PortalPageRequest.DefaultSize, sortKey, string.Equals(sortDirection, "descending", StringComparison.OrdinalIgnoreCase));

        [HttpGet("/api/v1/portal/members/{id:int}/requests")]
        [HttpGet("/api/v1/portal/members/requests")]
        [Produces("application/json")]
        public IActionResult MemberRequests(int id, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string sortKey, [FromQuery] string sortDirection, [FromQuery] RequestState? state, [FromQuery] ContentType? contentType) =>
            _portal.Member(id) is { } member ? Json(PortalPaging.Of(RequestStates.Filter(member.Requests, state, contentType), Asked(page, pageSize, sortKey, sortDirection), _sorts.Requests)) : NotFound();

        [HttpGet("/api/v1/portal/members/{id:int}/following")]
        [HttpGet("/api/v1/portal/members/following")]
        [Produces("application/json")]
        public IActionResult MemberFollowing(int id, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string sortKey, [FromQuery] string sortDirection, [FromQuery] RequestState? state, [FromQuery] ContentType? contentType) =>
            _portal.Member(id) is { } member ? Json(PortalPaging.Of(RequestStates.Filter(member.Following, state, contentType), Asked(page, pageSize, sortKey, sortDirection), _sorts.Requests)) : NotFound();

        [HttpGet("/api/v1/portal/members/{id:int}/removals")]
        [HttpGet("/api/v1/portal/members/removals")]
        [Produces("application/json")]
        public IActionResult MemberRemovals(int id, [FromQuery] int? page, [FromQuery] int? pageSize) =>
            _portal.Member(id) is { } member ? Json(PortalPaging.Of(member.Removals, Asked(page, pageSize, null, null), PortalSorts.None<PortalRemoval>())) : NotFound();

        [HttpGet("/api/v1/portal/members/{id:int}/history")]
        [HttpGet("/api/v1/portal/members/history")]
        [Produces("application/json")]
        public IActionResult MemberHistory(int id, [FromQuery] int? page, [FromQuery] int? pageSize) =>
            _portal.Member(id) is { } member ? Json(PortalPaging.Of(member.History, Asked(page, pageSize, null, null), PortalSorts.None<PortalEvent>())) : NotFound();

        [HttpGet("/api/v1/portal/requests/{id:int}/timeline")]
        [HttpGet("/api/v1/portal/requests/timeline")]
        [Produces("application/json")]
        public IActionResult RequestTimeline(int id, [FromQuery] int? page, [FromQuery] int? pageSize) =>
            _portal.Request(id) is { } request ? Json(PortalPaging.Of(request.Timeline, Asked(page, pageSize, null, null), PortalSorts.None<PortalEvent>())) : NotFound();

        [HttpGet("/api/v1/portal/requests/{id:int}/followers")]
        [HttpGet("/api/v1/portal/requests/followers")]
        [Produces("application/json")]
        public IActionResult RequestFollowers(int id, [FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string sortKey, [FromQuery] string sortDirection) =>
            _portal.Request(id) is { } request ? Json(PortalPaging.Of(request.Followers, Asked(page, pageSize, sortKey, sortDirection), PortalSorts.Followers)) : NotFound();

        [HttpGet("/api/v1/portal/queue/items")]
        [Produces("application/json")]
        public IActionResult QueueItems([FromQuery] int? page, [FromQuery] int? pageSize) =>
            Json(PortalPaging.Of(_portal.Queue(), Asked(page, pageSize, null, null), PortalSorts.None<PortalQueueItem>()));

        [HttpGet("/api/v1/portal/outages/history")]
        [Produces("application/json")]
        public IActionResult OutageHistory([FromQuery] int? page, [FromQuery] int? pageSize) =>
            Json(PortalPaging.Of(_records.Outages().History, Asked(page, pageSize, null, null), PortalSorts.None<PortalOutage>()));

        [HttpGet("/api/v1/portal/access-requests/waiting")]
        [Produces("application/json")]
        public IActionResult WaitingToJoin([FromQuery] int? page, [FromQuery] int? pageSize) =>
            Json(PortalPaging.Of(_records.AccessRequests().Waiting, Asked(page, pageSize, null, null), PortalSorts.None<PortalAccessRequest>()));

        [HttpGet("/api/v1/portal/access-requests/paused")]
        [Produces("application/json")]
        public IActionResult PausedToJoin([FromQuery] int? page, [FromQuery] int? pageSize) =>
            Json(PortalPaging.Of(_records.AccessRequests().Paused, Asked(page, pageSize, null, null), PortalSorts.None<PortalBlocked>()));
    }
}
