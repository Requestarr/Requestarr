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

        // Open removals of titles nobody requested, oldest first.
        IReadOnlyList<PortalTitleRemoval> TitleRemovals();
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
        private readonly IRemovalProposalRepository _proposals;
        private readonly IRemovalFlagRepository _flags;
        private readonly IMediaServerAccountRepository _mediaAccounts;
        private readonly IMediaServerAccessRequestRepository _mediaRequests;
        private readonly IAccessRequestRepository _accessRequests;
        private readonly IAdminQueue _queue;
        private readonly IAuditSettings _auditSettings;

        public PortalQueries(
            IRequestRepository requests,
            IRequestSubscriberRepository subscribers,
            IRequestarrUserRepository users,
            IUserIdentityRepository identities,
            IUserMediaAccessRepository access,
            IBackendRepository backends,
            IAuditRepository audit,
            IRemovalProposalRepository proposals,
            IRemovalFlagRepository flags,
            IMediaServerAccountRepository mediaAccounts,
            IMediaServerAccessRequestRepository mediaRequests,
            IAccessRequestRepository accessRequests,
            IAdminQueue queue,
            IAuditSettings auditSettings)
        {
            _requests = requests;
            _subscribers = subscribers;
            _users = users;
            _identities = identities;
            _access = access;
            _backends = backends;
            _audit = audit;
            _proposals = proposals;
            _flags = flags;
            _mediaAccounts = mediaAccounts;
            _mediaRequests = mediaRequests;
            _accessRequests = accessRequests;
            _queue = queue;
            _auditSettings = auditSettings;
        }

        public IReadOnlyList<PortalRequest> Requests()
        {
            var rows = new Rows(Names(), DiscordAccounts(), Backends(), _subscribers.All(), _proposals.All());

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
            var removals = _proposals.ForRequest(id);
            var latest = Latest(removals);
            var rows = new Rows(names, DiscordAccounts(), Backends(), _subscribers.ForRequest(id), removals);
            var removal = latest == null ? null : Removal(latest, names);

            return new PortalRequestDetail(
                rows.Of(request),
                request.DecisionReason,
                request.ProgressDetail,
                request.AvailableSeasons ?? new List<int>(),
                request.PosterUrl,
                removal,
                Timeline(request, names),
                Actions(request, latest),
                HistoryTrimmedAfter(request.Requested),
                rows.FollowersOf(request));
        }

        public IReadOnlyList<PortalMember> Members()
        {
            var all = _requests.All().ToList();
            var requests = all.ToLookup(r => r.OwnerUserId);
            var ids = all.Select(r => r.Id).ToHashSet();

            // Only follows of requests that still exist, as the member's own page lists them.
            var following = _subscribers.All().Where(s => ids.Contains(s.RequestId)).ToLookup(s => s.UserId);
            var instances = Instances();
            var accounts = DiscordAccounts();

            return _users.All()
                .OrderBy(u => u.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Select(u => Member(u, requests[u.Id].ToList(), following[u.Id].ToList(), instances, accounts))
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
            var accounts = DiscordAccounts();
            var rows = new Rows(names, accounts, Backends(), _subscribers.All(), _proposals.All());
            var all = _requests.All().ToDictionary(r => r.Id);
            var own = all.Values.Where(r => r.OwnerUserId == id).OrderByDescending(r => r.Requested).ToList();
            var follows = _subscribers.ForUser(id).Where(s => all.ContainsKey(s.RequestId)).ToList();
            var following = follows.Select(s => all[s.RequestId]).ToList();
            var removals = _proposals.All().Where(p => p.RequesterUserId == id || p.InitiatedByUserId == id).OrderByDescending(p => p.Created).Select(p => Removal(p, names)).ToList();
            var discordNames = PortalText.DiscordNames(_identities.All(), names);
            var history = _audit.ForTarget("user", id.ToString()).OrderBy(e => e.Timestamp).Select(e => PortalText.Event(e, names, discordNames)).ToList();

            var importedFrom = user.IsImported ? user.ImportedFrom ?? Imports.RequestrrSource.SourceName : null;

            return new PortalMemberDetail(
                Member(user, own, follows, instances, accounts),
                own.Select(rows.Of).ToList(),
                following.Select(rows.Of).ToList(),
                removals,
                MediaServer(id),
                history,
                HistoryTrimmedAfter(user.Added),
                importedFrom);
        }

        public IReadOnlyList<PortalQueueItem> Queue()
        {
            var names = Names();
            var accounts = DiscordAccounts();

            return _queue.Items().Select(i => QueueItem(i, names)).Where(i => i != null)
                .Select(i => i.MemberId.HasValue && accounts.GetValueOrDefault(i.MemberId.Value) is { } account
                    ? i with { WhoAvatar = account.AvatarUrl, WhoHandle = account.Username }
                    : i)
                .ToList();
        }

        public IReadOnlyList<PortalTitleRemoval> TitleRemovals()
        {
            var names = Names();

            return _proposals.All().Where(p => p.RequestId == null && p.IsOpen).OrderBy(p => p.Created).Select(p => new PortalTitleRemoval(p.Label, Removal(p, names))).ToList();
        }

        // Something that began before a trim's cutoff lost its first events to it: how long the log keeps entries now.
        private string HistoryTrimmedAfter(DateTime since) =>
            _auditSettings.TrimmedBefore is { } before && since < before ? _auditSettings.KeepFor : null;

        // The request's own history and its removals', oldest first. A request made before the audit log recorded
        // requests still starts with when it was asked for; an imported one starts with its import, dated then.
        private List<PortalEvent> Timeline(Request request, Dictionary<int, string> names)
        {
            var requested = PortalText.Action("request.create");
            var discordNames = PortalText.DiscordNames(_identities.All(), names);
            var events = _audit.ForTarget("request", request.Id.ToString())
                .Concat(_proposals.ForRequest(request.Id).SelectMany(p => _audit.ForTarget("removal", p.Id.ToString())))
                .Select(e => PortalText.Event(e, names, discordNames))
                .ToList();

            var imported = PortalText.Action(Imports.ImportService.ImportedAction);

            if (!events.Any(e => e.What == requested || e.What == imported))
            {
                events.Add(new PortalEvent(request.Requested, requested, names.GetValueOrDefault(request.OwnerUserId), null));
            }

            return events.OrderBy(e => e.At).ToList();
        }

        private PortalMember Member(
            RequestarrUser user,
            IReadOnlyCollection<Request> requests,
            IReadOnlyCollection<RequestSubscriber> follows,
            Dictionary<int, string> instances,
            Dictionary<int, UserIdentity> accounts)
        {
            var discord = accounts.GetValueOrDefault(user.Id);
            var access = _access.GetForUser(user.Id).OrderBy(a => a.ContentType).Select(a => Access(a, instances)).ToList();
            var active = requests.Count(r => r.IsActive);
            var pending = requests.Count(r => r.Status == RequestStatus.Pending);
            var asked = requests.Select(r => r.Requested).ToList();
            var byType = requests.GroupBy(r => r.ContentType).OrderBy(g => g.Key).Select(g => new PortalTypeCount(g.Key, g.Count())).ToList();

            return new PortalMember(
                user.Id,
                user.DisplayName,
                discord?.Username,
                discord?.ExternalId,
                user.Status,
                FirstSeen(user, asked, follows),
                access,
                requests.Count,
                active,
                pending,
                follows.Count,
                discord?.AvatarUrl,
                asked.Count > 0 ? asked.Min() : null,
                asked.Count > 0 ? asked.Max() : null,
                byType,
                discord?.BannerUrl,
                discord?.AccentColor);
        }

        // Someone known only from an import first appears in Requestarr's history with their earliest imported request, or
        // with their earliest follow when they only followed requests.
        private static DateTime FirstSeen(RequestarrUser user, IReadOnlyCollection<DateTime> asked, IEnumerable<RequestSubscriber> follows)
        {
            if (!user.IsImported)
            {
                return user.Added;
            }

            var dates = asked.Count > 0 ? asked : follows.Select(f => f.Added).ToList();

            return dates.Count > 0 ? dates.Min() : user.Added;
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

        // As RemovalService.LatestFor: the open removal, or else the newest. A request's page and its row both use it.
        private static RemovalProposal Latest(IEnumerable<RemovalProposal> removals) =>
            removals.Where(p => p.IsOpen).MaxBy(p => p.Id) ?? removals.MaxBy(p => p.Id);

        // The feed card's rules (RequestCards, RequestService): one place decides each, Discord and the portal ask it.
        private static List<string> Actions(Request request, RemovalProposal removal)
        {
            var actions = new List<string>();
            var trouble = TroubleSource.InTrouble(request);

            Add(actions, request.Status == RequestStatus.Pending, "approve", "deny");
            Add(actions, request.Status == RequestStatus.Pending || trouble, "available");
            Add(actions, RequestService.AdminCanCancel(request), "cancel");
            Add(actions, trouble && request.Status == RequestStatus.Approved && request.ExternalItemId.HasValue, "searchAgain");
            Add(actions, request.Status == RequestStatus.Failed, "retry");
            Add(actions, request is { ContentType: ContentType.Tv, IsActive: true }, "addSeasons");
            Add(actions, removal?.IsOpen == true && removal.Status != RemovalStatus.DeleteByHand, "forceRemoval", "dropRemoval");
            Add(actions, removal?.Status == RemovalStatus.DeleteByHand, "deletedByHand", "dropRemoval");
            Add(actions, removal?.IsOpen != true && (RequestCards.Downloaded(request) || RemovalService.IsRecordOnly(request)), "remove");

            return actions;
        }

        private static void Add(List<string> actions, bool when, params string[] names)
        {
            if (when)
            {
                actions.AddRange(names);
            }
        }

        private PortalRemoval Removal(RemovalProposal proposal, Dictionary<int, string> names)
        {
            var startedBy = NameNow(proposal.InitiatedByUserId, proposal.InitiatedByName, names);
            var closedBy = NameNow(proposal.ClosedByUserId, proposal.ClosedByName, names);
            var keeps = _flags.ForProposal(proposal.Id).Select(f => names.GetValueOrDefault(f.UserId)).ToList();
            var seasons = proposal.Seasons ?? new List<int>();

            return new PortalRemoval(proposal.Id, proposal.Status, seasons, proposal.Created, proposal.Closed, startedBy, closedBy, keeps, proposal.IsOpen, proposal.RecordOnly);
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
            'x' => _proposals.Find(item.Id) is { } removal ? RemovalItem(item, removal, names, Instances()) : null,
            'a' => _accessRequests.Find(item.Id) is { } access
                ? new PortalQueueItem(item.Key, "accessRequest", item.Since, access.Note ?? NoNote, access.DisplayName ?? access.Username, null, null)
                : null,
            _ => null
        };

        // A removal waits on the admin about its requester's title (their No, a vote they lost); one with no requester
        // names whoever started it. The name and the member page it opens are always the same person. One agreed while
        // deleting files is off is "delete it yourself", with the instance to delete it from.
        private static PortalQueueItem RemovalItem(QueueItem item, RemovalProposal removal, Dictionary<int, string> names, Dictionary<int, string> instances)
        {
            var memberId = removal.RequesterUserId ?? removal.InitiatedByUserId;
            var who = removal.RequesterUserId.HasValue ? names.GetValueOrDefault(removal.RequesterUserId.Value) : NameNow(removal.InitiatedByUserId, removal.InitiatedByName, names);

            if (removal.Status == RemovalStatus.DeleteByHand)
            {
                var instance = removal.BackendId.HasValue ? instances.GetValueOrDefault(removal.BackendId.Value) : null;
                return new PortalQueueItem(item.Key, "deleteByHand", item.Since, instance != null ? $"{removal.Label} · {instance}" : removal.Label, who, removal.RequestId, memberId);
            }

            return new PortalQueueItem(item.Key, "removal", item.Since, removal.Label, who, removal.RequestId, memberId) { RecordOnly = removal.RecordOnly };
        }

        private PortalQueueItem RequestItem(QueueItem item, string kind, Dictionary<int, string> names) =>
            _requests.Find(item.Id) is { } request
                ? new PortalQueueItem(item.Key, kind, item.Since, request.Label, names.GetValueOrDefault(request.OwnerUserId), request.Id, request.OwnerUserId)
                : null;

        // A member by their name now; the recorded name for an admin in the web app (who has no member record).
        private static string NameNow(int? userId, string recorded, Dictionary<int, string> names) =>
            userId.HasValue && names.TryGetValue(userId.Value, out var name) ? name : recorded;

        private Dictionary<int, string> Names() => _users.All().ToDictionary(u => u.Id, u => u.DisplayName);

        // Each member's Discord account, for their picture and @handle: one with a picture when they have several.
        private Dictionary<int, UserIdentity> DiscordAccounts() =>
            _identities.All().Where(i => i.Provider == UserIdentity.DiscordProvider).GroupBy(i => i.UserId)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault(i => i.AvatarUrl != null) ?? g.First());

        // Deleted ones too, named so, since requests keep showing where they went.
        private Dictionary<int, string> Instances() => _backends.Known().ToDictionary(b => b.Id, b => b.ShownName);

        private Dictionary<int, BackendInstance> Backends() => _backends.Known().ToDictionary(b => b.Id);

        // A request as a row: its requester and followers by name, its instance, and what the title is.
        private sealed class Rows
        {
            private readonly Dictionary<int, string> _names;
            private readonly Dictionary<int, UserIdentity> _accounts;
            private readonly Dictionary<int, BackendInstance> _instances;
            private readonly ILookup<int, RequestSubscriber> _followers;
            private readonly ILookup<int, RemovalProposal> _removals;

            public Rows(Dictionary<int, string> names, Dictionary<int, UserIdentity> accounts, Dictionary<int, BackendInstance> instances, IEnumerable<RequestSubscriber> followers, IEnumerable<RemovalProposal> removals)
            {
                _names = names;
                _accounts = accounts;
                _instances = instances;
                _followers = followers.ToLookup(s => s.RequestId);
                _removals = removals.Where(p => p.RequestId.HasValue).ToLookup(p => p.RequestId.Value);
            }

            public PortalRequest Of(Request request)
            {
                var followers = FollowersOf(request).Select(f => f.Person).ToList();
                var instance = request.BackendId.HasValue ? _instances.GetValueOrDefault(request.BackendId.Value) : null;
                var removal = Latest(_removals[request.Id]);

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
                    instance?.ShownName,
                    request.Seasons ?? new List<int>(),
                    request.Requested,
                    request.Decided,
                    request.Fulfilled,
                    Media(request, instance),
                    Actions(request, removal),
                    removal?.IsOpen == true ? removal.Id : null);
            }

            public List<PortalFollower> FollowersOf(Request request) =>
                _followers[request.Id].OrderBy(s => s.Added).Select(s => new PortalFollower(Person(s.UserId), s.Added)).ToList();

            private static PortalMedia Media(Request request, BackendInstance instance)
            {
                var media = request.Media;
                var open = TitleLinks.Open(instance, request);

                return new PortalMedia(
                    request.PosterUrl,
                    media?.Overview,
                    media?.InCinemas,
                    media?.DigitalRelease,
                    media?.PhysicalRelease,
                    media?.FirstAired,
                    media?.Network,
                    TitleLinks.Page(request),
                    open,
                    open == null ? null : instance.Implementation);
            }

            private PortalPerson Person(int userId)
            {
                var account = _accounts.GetValueOrDefault(userId);

                return new PortalPerson(userId, _names.GetValueOrDefault(userId), account?.AvatarUrl, account?.Username);
            }
        }
    }
}
