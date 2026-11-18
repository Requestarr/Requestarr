using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Access;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // One line of Requestarr's audit log as the portal shows it: what happened and who did it, and what it was about,
    // with the request or member page it opens.
    public record PortalAuditEntry(DateTime At, string What, string Who, string Detail, string About, int? RequestId, int? MemberId);

    // Each filter is optional: words the member, the title or the action contains, and the first and last day.
    public record PortalAuditFilter(string Member, string Title, string Action, DateTime? From, DateTime? To);

    public record PortalSearchResults(IReadOnlyList<PortalRequest> Requests, IReadOnlyList<PortalMember> Members);

    public interface IPortalAuditLog
    {
        // Newest first, at most a page.
        IReadOnlyList<PortalAuditEntry> Find(PortalAuditFilter filter);

        // The header search: requests by title, members by name or Discord username.
        PortalSearchResults Search(string term);
    }

    public class PortalAuditLog : IPortalAuditLog
    {
        // As many lines as one page shows; a narrower filter finds older ones.
        public const int PageSize = 500;

        // Results per kind in the header search.
        public const int SearchLimit = 8;

        private readonly IAuditRepository _audit;
        private readonly IRequestRepository _requests;
        private readonly IRequestarrUserRepository _users;
        private readonly IRemovalProposalRepository _removals;
        private readonly IAccessRequestRepository _accessRequests;
        private readonly IBackendRepository _backends;
        private readonly IPortalQueries _queries;

        public PortalAuditLog(IAuditRepository audit,
                              IRequestRepository requests,
                              IRequestarrUserRepository users,
                              IRemovalProposalRepository removals,
                              IAccessRequestRepository accessRequests,
                              IBackendRepository backends,
                              IPortalQueries queries)
        {
            _audit = audit;
            _requests = requests;
            _users = users;
            _removals = removals;
            _accessRequests = accessRequests;
            _backends = backends;
            _queries = queries;
        }

        public IReadOnlyList<PortalAuditEntry> Find(PortalAuditFilter filter)
        {
            var users = _users.All();
            var about = new Subjects(_requests.All(), users, _removals.All(), _accessRequests.All(), _backends.All());
            var names = users.ToDictionary(u => u.Id, u => u.DisplayName);

            return _audit.All()
                .Where(e => (filter.From == null || e.Timestamp >= filter.From.Value.Date) && (filter.To == null || e.Timestamp < filter.To.Value.Date.AddDays(1)))
                .OrderByDescending(e => e.Id)
                .Select(e => about.Line(e, PortalText.Event(e, names)))
                .Where(l => Contains(l.Entry.Who, filter.Member) || (l.Entry.MemberId.HasValue && Contains(l.Entry.About, filter.Member)))
                .Where(l => l.IsTitle ? Contains(l.Entry.About, filter.Title) : string.IsNullOrWhiteSpace(filter.Title))
                .Where(l => Contains(l.Entry.What, filter.Action))
                .Select(l => l.Entry)
                .Take(PageSize)
                .ToList();
        }

        public PortalSearchResults Search(string term)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return new PortalSearchResults(new List<PortalRequest>(), new List<PortalMember>());
            }

            var requests = _queries.Requests().Where(r => Contains(r.Title, term)).Take(SearchLimit).ToList();
            var members = _queries.Members().Where(m => Contains(m.Name, term) || Contains(m.DiscordName, term)).Take(SearchLimit).ToList();

            return new PortalSearchResults(requests, members);
        }

        // An empty filter matches everything.
        private static bool Contains(string value, string filter) =>
            string.IsNullOrWhiteSpace(filter) || (value ?? string.Empty).Contains(filter.Trim(), StringComparison.OrdinalIgnoreCase);

        // What each kind of entry is about, by the target it recorded.
        private sealed class Subjects
        {
            private readonly Dictionary<int, Request> _requests;
            private readonly Dictionary<int, RequestarrUser> _users;
            private readonly Dictionary<int, RemovalProposal> _removals;
            private readonly Dictionary<int, AccessRequest> _accessRequests;
            private readonly Dictionary<int, BackendInstance> _instances;

            public Subjects(IEnumerable<Request> requests, IEnumerable<RequestarrUser> users, IEnumerable<RemovalProposal> removals, IEnumerable<AccessRequest> accessRequests, IEnumerable<BackendInstance> instances)
            {
                _requests = requests.ToDictionary(r => r.Id);
                _users = users.ToDictionary(u => u.Id);
                _removals = removals.ToDictionary(r => r.Id);
                _accessRequests = accessRequests.ToDictionary(r => r.Id);
                _instances = instances.ToDictionary(i => i.Id);
            }

            // IsTitle: the entry is about a title (a request or a removal), which the title filter searches.
            public (PortalAuditEntry Entry, bool IsTitle) Line(AuditEntry entry, PortalEvent shown)
            {
                var id = int.TryParse(entry.TargetId, out var parsed) ? parsed : (int?)null;
                var (about, requestId, memberId) = About(entry.TargetType, id, entry.TargetId);

                return (new PortalAuditEntry(shown.At, shown.What, shown.Who, shown.Detail, about, requestId, memberId), entry.TargetType is "request" or "removal" or "title");
            }

            private (string About, int? RequestId, int? MemberId) About(string type, int? id, string raw) => type switch
            {
                "request" when id.HasValue && _requests.TryGetValue(id.Value, out var request) => (request.Label, request.Id, null),
                "removal" when id.HasValue && _removals.TryGetValue(id.Value, out var removal) => (removal.Label, removal.RequestId, null),
                "user" when id.HasValue && _users.TryGetValue(id.Value, out var user) => (user.DisplayName, null, user.Id),
                "access-request" when id.HasValue && _accessRequests.TryGetValue(id.Value, out var asked) => (asked.DisplayName ?? asked.Username, null, null),
                "instance" when id.HasValue && _instances.TryGetValue(id.Value, out var instance) => (instance.Name, null, null),
                "settings" => ("Settings", null, null),
                _ => (raw, null, null)
            };
        }
    }
}
