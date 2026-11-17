using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    public interface IPortalQueries
    {
        IReadOnlyList<PortalRequest> Requests();
        PortalRequestDetail Request(int id);
        IReadOnlyList<PortalMember> Members();
        PortalMemberDetail Member(int id);
        IReadOnlyList<PortalQueueItem> Queue();
    }

    // The admin portal's read views (task 5a). Everything comes from Requestarr's own records; nothing here asks
    // Discord, Sonarr or Radarr, so the pages load fast and show what Requestarr knows.
    public class PortalQueries : IPortalQueries
    {
        // An access request's title when the person wrote no note.
        private const string NoNote = "No note";

        private static readonly TimeSpan DefaultQuotaWindow = TimeSpan.FromDays(7);

        private readonly IRequestRepository _requests;
        private readonly IRequestSubscriberRepository _subscribers;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly IUserMediaAccessRepository _access;
        private readonly IBackendRepository _backends;
        private readonly IAuditRepository _audit;
        private readonly IRemovalService _removals;
        private readonly IRemovalProposalRepository _proposals;
        private readonly IMediaServerAccountRepository _mediaAccounts;
        private readonly IMediaServerAccessRequestRepository _mediaRequests;
        private readonly IAccessRequestRepository _accessRequests;
        private readonly IAdminQueue _queue;

        public PortalQueries(
            IRequestRepository requests,
            IRequestSubscriberRepository subscribers,
            IRequestarrUserRepository users,
            IUserIdentityRepository identities,
            IUserMediaAccessRepository access,
            IBackendRepository backends,
            IAuditRepository audit,
            IRemovalService removals,
            IRemovalProposalRepository proposals,
            IMediaServerAccountRepository mediaAccounts,
            IMediaServerAccessRequestRepository mediaRequests,
            IAccessRequestRepository accessRequests,
            IAdminQueue queue)
        {
            _requests = requests;
            _subscribers = subscribers;
            _users = users;
            _identities = identities;
            _access = access;
            _backends = backends;
            _audit = audit;
            _removals = removals;
            _proposals = proposals;
            _mediaAccounts = mediaAccounts;
            _mediaRequests = mediaRequests;
            _accessRequests = accessRequests;
            _queue = queue;
        }

        public IReadOnlyList<PortalRequest> Requests()
        {
            var rows = new Rows(Names(), Instances(), _subscribers.All());

            return _requests.All().OrderByDescending(r => r.Requested).Select(rows.Of).ToList();
        }

        public PortalRequestDetail Request(int id)
        {
            var request = _requests.Find(id);

            if (request == null)
            {
                return null;
            }

            var names = Names();
            var latest = _removals.LatestFor(id);
            var row = new Rows(names, Instances(), _subscribers.ForRequest(id)).Of(request);
            var removal = latest == null ? null : Removal(latest, names);

            return new PortalRequestDetail(row, request.DecisionReason, request.ProgressDetail, request.AvailableSeasons ?? new List<int>(), request.PosterUrl, removal, Timeline(request, names));
        }

        public IReadOnlyList<PortalMember> Members()
        {
            var all = _requests.All().ToList();
            var requests = all.ToLookup(r => r.OwnerUserId);
            var ids = all.Select(r => r.Id).ToHashSet();

            // Only follows of requests that still exist, as the member's own page lists them.
            var following = _subscribers.All().Where(s => ids.Contains(s.RequestId)).ToLookup(s => s.UserId);
            var instances = Instances();

            return _users.All()
                .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(u => Member(u, requests[u.Id].ToList(), following[u.Id].Count(), instances))
                .ToList();
        }

        public PortalMemberDetail Member(int id)
        {
            var user = _users.Find(id);

            if (user == null)
            {
                return null;
            }

            var names = Names();
            var instances = Instances();
            var rows = new Rows(names, instances, _subscribers.All());
            var all = _requests.All().ToDictionary(r => r.Id);
            var own = all.Values.Where(r => r.OwnerUserId == id).OrderByDescending(r => r.Requested).ToList();
            var following = _subscribers.ForUser(id).Select(s => all.GetValueOrDefault(s.RequestId)).Where(r => r != null).ToList();
            var removals = _proposals.All().Where(p => p.RequesterUserId == id || p.InitiatedByUserId == id).OrderByDescending(p => p.Created).Select(p => Removal(p, names)).ToList();
            var history = _audit.ForTarget("user", id.ToString()).OrderBy(e => e.Timestamp).Select(e => PortalText.Event(e, names)).ToList();

            return new PortalMemberDetail(Member(user, own, following.Count, instances), own.Select(rows.Of).ToList(), following.Select(rows.Of).ToList(), removals, MediaServer(id), history);
        }

        public IReadOnlyList<PortalQueueItem> Queue()
        {
            var names = Names();

            return _queue.Items().Select(i => QueueItem(i, names)).Where(i => i != null).ToList();
        }

        // The request's own history and its removals', oldest first. A request made before the audit log recorded
        // requests still starts with when it was asked for.
        private List<PortalEvent> Timeline(Request request, Dictionary<int, string> names)
        {
            var requested = PortalText.Action("request.create");
            var events = _audit.ForTarget("request", request.Id.ToString())
                .Concat(_proposals.ForRequest(request.Id).SelectMany(p => _audit.ForTarget("removal", p.Id.ToString())))
                .Select(e => PortalText.Event(e, names))
                .ToList();

            if (!events.Any(e => e.What == requested))
            {
                events.Add(new PortalEvent(request.Requested, requested, names.GetValueOrDefault(request.OwnerUserId), null));
            }

            return events.OrderBy(e => e.At).ToList();
        }

        private PortalMember Member(RequestarrUser user, IReadOnlyCollection<Request> requests, int following, Dictionary<int, string> instances)
        {
            var discord = _identities.GetForUser(user.Id).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider);
            var access = _access.GetForUser(user.Id).OrderBy(a => a.ContentType).Select(a => Access(a, instances)).ToList();
            var active = requests.Count(r => r.IsActive);
            var pending = requests.Count(r => r.Status == RequestStatus.Pending);

            return new PortalMember(user.Id, user.DisplayName, discord?.Username, user.Status, user.Added, access, requests.Count, active, pending, following);
        }

        private static PortalAccess Access(UserMediaAccess access, Dictionary<int, string> instances)
        {
            // A limit without its own window counts the last 7 days, as requests and the member card do.
            int? days = access.QuotaLimit.HasValue ? (int)(access.QuotaWindow ?? DefaultQuotaWindow).TotalDays : null;
            var instance = access.BackendId.HasValue ? instances.GetValueOrDefault(access.BackendId.Value) : null;

            return new PortalAccess(access.ContentType, access.State, access.QuotaLimit, days, instance);
        }

        private string MediaServer(int userId)
        {
            var linked = _mediaAccounts.GetForUser(userId).FirstOrDefault();
            return linked == null ? null : $"{linked.Provider}: {linked.Username}";
        }

        private static PortalRemoval Removal(RemovalProposal proposal, Dictionary<int, string> names)
        {
            var startedBy = proposal.InitiatedByName;

            if (startedBy == null && proposal.InitiatedByUserId.HasValue)
            {
                startedBy = names.GetValueOrDefault(proposal.InitiatedByUserId.Value);
            }

            return new PortalRemoval(proposal.Id, proposal.Status, proposal.Seasons ?? new List<int>(), proposal.Created, proposal.Closed, startedBy, proposal.ClosedByName);
        }

        // What the admin channel's queue item is about, in the portal's words; null when it's gone since.
        private PortalQueueItem QueueItem(QueueItem item, Dictionary<int, string> names) => item.Kind switch
        {
            'r' => RequestItem(item, "request", names),
            't' => RequestItem(item, "trouble", names),
            'p' => _mediaRequests.Find(item.Id) is { } link
                ? new PortalQueueItem(item.Key, "mediaServerLink", item.Since, $"{link.Provider}: {link.Username}", names.GetValueOrDefault(link.UserId), null, link.UserId)
                : null,
            'i' => _backends.Find(item.Id) is { } instance ? new PortalQueueItem(item.Key, "instanceDown", item.Since, instance.Name, null, null, null) : null,
            'x' => _proposals.Find(item.Id) is { } removal ? RemovalItem(item, removal, names) : null,
            'a' => _accessRequests.Find(item.Id) is { } access
                ? new PortalQueueItem(item.Key, "accessRequest", item.Since, access.Note ?? NoNote, access.DisplayName ?? access.Username, null, null)
                : null,
            _ => null
        };

        // A removal waits on the admin about its requester's title (their No, a vote they lost); one with no requester
        // names whoever started it. The name and the member page it opens are always the same person.
        private static PortalQueueItem RemovalItem(QueueItem item, RemovalProposal removal, Dictionary<int, string> names)
        {
            var memberId = removal.RequesterUserId ?? removal.InitiatedByUserId;
            var who = removal.RequesterUserId.HasValue ? names.GetValueOrDefault(removal.RequesterUserId.Value) : removal.InitiatedByName;

            return new PortalQueueItem(item.Key, "removal", item.Since, removal.Label, who, removal.RequestId, memberId);
        }

        private PortalQueueItem RequestItem(QueueItem item, string kind, Dictionary<int, string> names) =>
            _requests.Find(item.Id) is { } request
                ? new PortalQueueItem(item.Key, kind, item.Since, request.Label, names.GetValueOrDefault(request.OwnerUserId), request.Id, request.OwnerUserId)
                : null;

        private Dictionary<int, string> Names() => _users.All().ToDictionary(u => u.Id, u => u.DisplayName);

        private Dictionary<int, string> Instances() => _backends.All().ToDictionary(b => b.Id, b => b.Name);

        // A request as a row: its requester and followers by name, and its instance.
        private sealed class Rows
        {
            private readonly Dictionary<int, string> _names;
            private readonly Dictionary<int, string> _instances;
            private readonly ILookup<int, RequestSubscriber> _followers;

            public Rows(Dictionary<int, string> names, Dictionary<int, string> instances, IEnumerable<RequestSubscriber> followers)
            {
                _names = names;
                _instances = instances;
                _followers = followers.ToLookup(s => s.RequestId);
            }

            public PortalRequest Of(Request request)
            {
                var followers = _followers[request.Id].OrderBy(s => s.Added).Select(s => Person(s.UserId)).ToList();
                var instance = request.BackendId.HasValue ? _instances.GetValueOrDefault(request.BackendId.Value) : null;

                return new PortalRequest(
                    request.Id,
                    request.Title,
                    request.Year,
                    request.ContentType,
                    request.Status,
                    request.Progress,
                    Person(request.OwnerUserId),
                    followers,
                    request.BackendId,
                    instance,
                    request.Seasons ?? new List<int>(),
                    request.Requested,
                    request.Decided,
                    request.Fulfilled);
            }

            private PortalPerson Person(int userId) => new(userId, _names.GetValueOrDefault(userId));
        }
    }
}
