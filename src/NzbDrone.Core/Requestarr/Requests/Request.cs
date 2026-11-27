using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // Anime only: which audio the series is steered to on its Sonarr instance.
    public enum AnimeLanguage
    {
        Dub = 1,
        Sub = 2
    }

    public enum RequestStatus
    {
        Pending = 0,
        Approved = 1,
        Denied = 2,
        Cancelled = 3,
        Failed = 4,
        Available = 5,

        // Removed from the library by a removal (vote or admin Force); a later request is a fresh one.
        Removed = 6,

        // Imported from another bot's history for a title no Sonarr or Radarr had: kept as history only. Nothing is sent
        // anywhere, and a new request for the title is made as usual. A later import links it once the library has it.
        NotInLibrary = 7
    }

    // What Sonarr / Radarr are doing with a sent request (Status stays Approved until it's all there).
    public enum DownloadProgress
    {
        None = 0,
        Downloading = 1,
        Stalled = 2,
        Failed = 3,

        // Status Available because it was downloaded and imported (not already in the library).
        Imported = 4
    }

    public class Request : ModelBase
    {
        public int OwnerUserId { get; set; }
        public ContentType ContentType { get; set; }
        public string Title { get; set; }
        public int? Year { get; set; }
        public int? TmdbId { get; set; }
        public int? TvdbId { get; set; }
        public string MusicBrainzId { get; set; }
        public string ComicVineId { get; set; }
        public string Isbn { get; set; }
        public RequestStatus Status { get; set; }
        public int? BackendId { get; set; }
        public int? QualityProfileId { get; set; }
        public string RootFolderPath { get; set; }
        public List<int> Seasons { get; set; } = new();
        public bool MonitorFutureSeasons { get; set; }

        // TV asked for as "All seasons", with new seasons as they air: its cards say all seasons, and there's nothing left
        // to add but specials. Seasons holds every season the series had when it was last sent.
        public bool AllSeasons { get; set; }
        public int? DecidedByUserId { get; set; }

        // The deciding admin's name when they aren't a member (the web app); Discord's cards show it.
        public string DecidedByName { get; set; }
        public string DecisionReason { get; set; }
        public int? ExternalItemId { get; set; }

        // The series fix it was added to Sonarr with (its reason), shown on its card; null without one.
        public string AppliedFix { get; set; }
        public DateTime Requested { get; set; }
        public DateTime? Decided { get; set; }
        public DateTime? Fulfilled { get; set; }

        // The user's request message and the admin approval card (Discord message IDs), edited
        // to the real state after a decision. Empty on requests made before they were stored.
        public string MessageChannelId { get; set; }
        public string MessageId { get; set; }
        public string AdminMessageId { get; set; }

        public AnimeLanguage? Language { get; set; }

        // Written by an import from another bot's history (Imports.ImportService): its history is posted in Discord.
        public bool Imported { get; set; }

        // An imported request in the library, until Sonarr or Radarr is first asked about it: what it's doing then was
        // already so before the import, and is recorded without telling anyone.
        public bool FirstCheckDue { get; set; }

        // Until the import's post in the admin feed has covered it (Imports.ImportSummary).
        public bool SummaryDue { get; set; }

        // Download progress, from Sonarr's / Radarr's webhook and the backup poll. TV: the requested
        // seasons whose aired episodes all have files. ProgressSizeLeft / ProgressSince spot a download
        // that stops moving.
        public DownloadProgress Progress { get; set; }
        public string ProgressDetail { get; set; }
        public long? ProgressSizeLeft { get; set; }
        public DateTime? ProgressSince { get; set; }
        public List<int> AvailableSeasons { get; set; } = new();

        // The request's card in the admin feed channel, reposted on every change.
        public string FeedMessageId { get; set; }

        // The poster as the backend gave it when the request was made, for the feed card thumbnail.
        public string PosterUrl { get; set; }

        // What the title is (overview, dates, its slug in Sonarr or Radarr); null until it has been looked up.
        public RequestMedia Media { get; set; }

        public int? ExternalId => ContentType == ContentType.Tv ? TvdbId : TmdbId;

        public bool IsActive => Status == RequestStatus.Pending || Status == RequestStatus.Approved || Status == RequestStatus.Available;

        // Sent and not finished: Sonarr / Radarr are searching, downloading, or partly there.
        public bool IsTracked => Status == RequestStatus.Approved && ExternalItemId.HasValue;

        public string Label => Titles.WithYear(Title, Year);
    }

    public class RequestSubscriber : ModelBase
    {
        public int RequestId { get; set; }
        public int UserId { get; set; }
        public DateTime Added { get; set; }

        // The follower's own message about this request, reposted when the request changes.
        public string MessageChannelId { get; set; }
        public string MessageId { get; set; }
    }

    public interface IRequestRepository : IBasicRepository<Request>
    {
        List<Request> ForTitle(ContentType type, int externalId);
        List<Request> OwnedBy(int userId);
        List<Request> WithStatus(RequestStatus status);
    }

    public class RequestRepository : BasicRepository<Request>, IRequestRepository
    {
        // Each change tells the web app's open pages to read again (Requestarr.Http PortalBroadcaster), as Servarr's
        // resources do.
        protected override bool PublishModelEvents => true;

        public RequestRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<Request> ForTitle(ContentType type, int externalId)
        {
            return type == ContentType.Tv
                ? Query(r => r.ContentType == type && r.TvdbId == externalId)
                : Query(r => r.ContentType == type && r.TmdbId == externalId);
        }

        public List<Request> OwnedBy(int userId) => Query(r => r.OwnerUserId == userId);

        public List<Request> WithStatus(RequestStatus status) => Query(r => r.Status == status);
    }

    public interface IRequestSubscriberRepository : IBasicRepository<RequestSubscriber>
    {
        List<RequestSubscriber> ForRequest(int requestId);
        List<RequestSubscriber> ForUser(int userId);
    }

    public class RequestSubscriberRepository : BasicRepository<RequestSubscriber>, IRequestSubscriberRepository
    {
        // Each change tells the web app's open pages to read again (Requestarr.Http PortalBroadcaster), as Servarr's
        // resources do.
        protected override bool PublishModelEvents => true;

        public RequestSubscriberRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<RequestSubscriber> ForRequest(int requestId) => Query(s => s.RequestId == requestId);

        public List<RequestSubscriber> ForUser(int userId) => Query(s => s.UserId == userId);
    }

    public static class Titles
    {
        private static readonly Regex EndsInYear = new(@"\(\d{4}\)\s*$");

        // Backends already put the year in some titles ("Dark Matter (2024)"); never add one after it.
        public static string WithYear(string title, int? year)
        {
            if (!year.HasValue || string.IsNullOrEmpty(title) || EndsInYear.IsMatch(title))
            {
                return title;
            }

            return $"{title} ({year})";
        }
    }

    public static class RequestOrdering
    {
        public static IEnumerable<Request> Newest(this IEnumerable<Request> requests) => requests.OrderByDescending(r => r.Requested).ThenByDescending(r => r.Id);
    }
}
