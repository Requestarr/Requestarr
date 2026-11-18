using System;
using System.Collections.Generic;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // What the admin portal's pages show (docs/admin-portal.md): read views of requests, members and the queue, built
    // from Requestarr's own records. Names are as Discord shows them.
    public record PortalPerson(int Id, string Name);

    // One request: its requester, and everyone who asked for the same title after them (its followers).
    public record PortalRequest(int Id, string Title, int? Year, ContentType ContentType, RequestStatus Status, DownloadProgress Progress, PortalPerson Requester,
                                IReadOnlyList<PortalPerson> Followers, int? InstanceId, string Instance, IReadOnlyList<int> Seasons, DateTime Requested, DateTime? Decided, DateTime? Fulfilled);

    // Something that happened, from the audit log: when, what, who did it and any detail (a reason, seasons).
    public record PortalEvent(DateTime At, string What, string Who, string Detail);

    // Keeps: the members who pressed Keep it on its vote. IsOpen: an admin can still force or drop it.
    public record PortalRemoval(int Id, RemovalStatus Status, IReadOnlyList<int> Seasons, DateTime Created, DateTime? Closed, string StartedBy, string ClosedBy,
                                IReadOnlyList<string> Keeps, bool IsOpen);

    // An open removal of a title nobody requested through Requestarr, which has no request page: its own feed card in
    // Discord.
    public record PortalTitleRemoval(string Title, PortalRemoval Removal);

    // Actions: what an admin can do to it now, as its feed card in Discord offers (approve, deny, available, cancel,
    // searchAgain, retry, addSeasons, remove, forceRemoval, dropRemoval). HistoryTrimmedAfter: how long the audit log keeps
    // an entry ("1 year") when the request is older than that, so its first events may have been trimmed; otherwise null.
    public record PortalRequestDetail(PortalRequest Request, string DecisionReason, string ProgressDetail, IReadOnlyList<int> AvailableSeasons, string PosterUrl,
                                      PortalRemoval LatestRemoval, IReadOnlyList<PortalEvent> Timeline, IReadOnlyList<string> Actions, string HistoryTrimmedAfter);

    // A member's access to one content type: its level, their limit (requests per so many days) and their instance.
    public record PortalAccess(ContentType ContentType, AccessState State, int? QuotaLimit, int? QuotaDays, string Instance);

    public record PortalMember(int Id, string Name, string DiscordName, string DiscordId, UserStatus Status, DateTime Added, IReadOnlyList<PortalAccess> Access,
                               int Requests, int Active, int Pending, int Following);

    // MediaServer: the linked account ("Plex: name"), or null. HistoryTrimmedAfter: as on a request's page.
    public record PortalMemberDetail(PortalMember Member, IReadOnlyList<PortalRequest> Requests, IReadOnlyList<PortalRequest> Following,
                                     IReadOnlyList<PortalRemoval> Removals, string MediaServer, IReadOnlyList<PortalEvent> History, string HistoryTrimmedAfter);

    // One thing waiting on an admin, as the admin channel's queue has it. Kind: request, trouble, mediaServerLink,
    // instanceDown, removal or accessRequest; RequestId or MemberId says which page it opens.
    public record PortalQueueItem(string Key, string Kind, DateTime Since, string Title, string Who, int? RequestId, int? MemberId);
}
