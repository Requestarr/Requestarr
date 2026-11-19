using System;
using System.Collections.Generic;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // What the admin portal's pages show (docs/admin-portal.md): read views of requests, members and the queue, built
    // from Requestarr's own records. Names are as Discord shows them.
    // Avatar: their Discord picture's address, or null for Discord's default one.
    public record PortalPerson(int Id, string Name, string Avatar);

    // One request: its requester, and everyone who asked for the same title after them (its followers).
    public record PortalRequest(int Id, string Title, int? Year, ContentType ContentType, RequestStatus Status, DownloadProgress Progress, PortalPerson Requester,
                                IReadOnlyList<PortalPerson> Followers, int? InstanceId, string Instance, IReadOnlyList<int> Seasons, DateTime Requested, DateTime? Decided, DateTime? Fulfilled,
                                PortalMedia Media);

    // What the title is, as the feed card shows it: the poster, overview and dates, its page on The Movie Database or
    // TheTVDB, and (when it's in a Sonarr or Radarr a browser can reach) its page there. Values not known are left out.
    public record PortalMedia(string Poster, string Overview, DateTime? InCinemas, DateTime? DigitalRelease, DateTime? PhysicalRelease, DateTime? FirstAired,
                              string Network, string Page, string OpenUrl, string OpenIn);

    // Something that happened, from the audit log: when, what, who did it and any detail (a reason, seasons).
    public record PortalEvent(DateTime At, string What, string Who, string Detail);

    // Keeps: the members who pressed Keep it on its vote. IsOpen: an admin can still force or drop it.
    public record PortalRemoval(int Id, RemovalStatus Status, IReadOnlyList<int> Seasons, DateTime Created, DateTime? Closed, string StartedBy, string ClosedBy,
                                IReadOnlyList<string> Keeps, bool IsOpen);

    // An open removal of a title nobody requested through Requestarr, which has no request page: its own feed card in
    // Discord.
    public record PortalTitleRemoval(string Title, PortalRemoval Removal);

    // Actions: what an admin can do to it now, as its feed card in Discord offers (approve, deny, available, cancel,
    // searchAgain, retry, addSeasons, remove, forceRemoval or deletedByHand, dropRemoval). HistoryTrimmedAfter: how long the audit log keeps
    // an entry now ("1 year", or "forever" once trimmed before) when its first events were trimmed; otherwise null.
    public record PortalRequestDetail(PortalRequest Request, string DecisionReason, string ProgressDetail, IReadOnlyList<int> AvailableSeasons, string PosterUrl,
                                      PortalRemoval LatestRemoval, IReadOnlyList<PortalEvent> Timeline, IReadOnlyList<string> Actions, string HistoryTrimmedAfter);

    // A member's access to one content type: its level, their limit (requests per so many days) and their instance.
    public record PortalAccess(ContentType ContentType, AccessState State, int? QuotaLimit, int? QuotaDays, string Instance);

    // Added: when they were added, or for someone known only from an import, when they first asked for anything (the
    // import itself is just when Requestarr heard of it). FirstRequest and LastRequest: their own requests'; ByType: how
    // many of their requests are of each type.
    public record PortalMember(int Id, string Name, string DiscordName, string DiscordId, UserStatus Status, DateTime Added, IReadOnlyList<PortalAccess> Access,
                               int Requests, int Active, int Pending, int Following, string Avatar, DateTime? FirstRequest, DateTime? LastRequest,
                               IReadOnlyList<PortalTypeCount> ByType);

    public record PortalTypeCount(ContentType ContentType, int Count);

    // MediaServer: the linked account ("Plex: name"), or null. HistoryTrimmedAfter: as on a request's page.
    // ImportedFrom: where an imported person's history came from ("#media-requests in The CheaZiest"), or null.
    public record PortalMemberDetail(PortalMember Member, IReadOnlyList<PortalRequest> Requests, IReadOnlyList<PortalRequest> Following,
                                     IReadOnlyList<PortalRemoval> Removals, string MediaServer, IReadOnlyList<PortalEvent> History, string HistoryTrimmedAfter,
                                     string ImportedFrom);

    // One thing waiting on an admin, as the admin channel's queue has it. Kind: request, trouble, mediaServerLink,
    // instanceDown, removal or accessRequest; RequestId or MemberId says which page it opens.
    public record PortalQueueItem(string Key, string Kind, DateTime Since, string Title, string Who, int? RequestId, int? MemberId, string WhoAvatar = null);
}
