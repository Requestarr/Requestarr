using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Common.Http;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Backends;
using Requestarr.Core.Metadata;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    public interface IRequestMediaService
    {
        // Looks up what each request without it is, in the background (a start, an import, every few hours).
        void FillInBackground();

        // The same, now; the number looked up.
        Task<int> FillAsync();
    }

    // Requests made before media was kept, and imported ones, get it here: looked up once in a Sonarr or Radarr (whose
    // own lookups ask The Movie Database and TheTVDB), one at a time and a moment apart, then kept with the request.
    // A title the instance answers it can't find is marked looked up, so it's never asked again; a Sonarr or Radarr
    // that can't be reached is left for the next run, and skipped for the rest of this one. A request in an instance's
    // library is read from that library, which always knows the title's page, and read again while its page isn't known.
    // It runs after a start, after an import, and every few hours, which also fills in a request whose media another
    // change wrote over.
    public class RequestMediaService : IRequestMediaService, IHandle<ApplicationStartedEvent>
    {
        public static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(500);

        // After a start, the instances are in place and checked first.
        public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan Every = TimeSpan.FromHours(6);

        private readonly SemaphoreSlim _running = new(1, 1);
        private readonly IRequestRepository _requests;
        private readonly IBackendService _backends;
        private readonly IRadarrApi _radarr;
        private readonly ISonarrApi _sonarr;
        private readonly ITmdbClient _tmdb;
        private readonly Logger _logger;

        // Requests The Movie Database was asked for a year since Requestarr started: one it has no date for isn't asked
        // again until the next start.
        private readonly HashSet<int> _askedForYear = new();

        // Asked while a run was going: that run reads the requests again when it's done.
        private int _again;

        public RequestMediaService(IRequestRepository requests, IBackendService backends, IRadarrApi radarr, ISonarrApi sonarr, ITmdbClient tmdb, Logger logger)
        {
            _requests = requests;
            _backends = backends;
            _radarr = radarr;
            _sonarr = sonarr;
            _tmdb = tmdb;
            _logger = logger;
        }

        public TimeSpan Delay { get; set; } = Pace;

        public void Handle(ApplicationStartedEvent message) => _ = Task.Run(async () =>
        {
            await Task.Delay(StartDelay);
            using var timer = new PeriodicTimer(Every);

            do
            {
                await FillQuietlyAsync();
            }
            while (await timer.WaitForNextTickAsync());
        });

        public void FillInBackground() => _ = Task.Run(FillQuietlyAsync);

        private async Task FillQuietlyAsync()
        {
            try
            {
                await FillAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Looking up requests' media stopped; the next run carries on");
            }
        }

        public async Task<int> FillAsync()
        {
            Interlocked.Exchange(ref _again, 1);

            if (!await _running.WaitAsync(0))
            {
                return 0;
            }

            try
            {
                var filled = 0;

                while (Interlocked.Exchange(ref _again, 0) == 1)
                {
                    filled += await FillWaitingAsync();
                }

                var years = await FillYearsAsync();

                if (years > 0)
                {
                    _logger.Info("Filled in the year for {0} requests from their release dates", years);
                }

                if (filled > 0)
                {
                    _logger.Info("Looked up media for {0} requests", filled);
                }

                return filled;
            }
            finally
            {
                _running.Release();
            }
        }

        // Requests without a year (older Requestrr replies imported without one): the year their title first came out, so
        // they're shown as "Smart House (1999)". From the release dates Sonarr or Radarr gave, else from The Movie Database
        // by the title's ID (a title Sonarr or Radarr has no date for yet, such as Hollow Trees).
        private async Task<int> FillYearsAsync()
        {
            var filled = 0;

            foreach (var request in _requests.All().Where(r => r.Year == null && (r.Media != null || r.TmdbId.HasValue || r.TvdbId.HasValue)).ToList())
            {
                request.Year = request.Media?.FirstYear() ?? await AskYearAsync(request);

                if (request.Year != null)
                {
                    _requests.SetFields(request, r => r.Year);
                    filled++;
                }
            }

            return filled;
        }

        // The Movie Database's year for a title, once a start, a moment apart as the media lookups are.
        private async Task<int?> AskYearAsync(Request request)
        {
            if (!_askedForYear.Add(request.Id))
            {
                return null;
            }

            var year = await _tmdb.YearAsync(request.ContentType, request.TmdbId, request.TvdbId, CancellationToken.None);
            await Task.Delay(Delay);

            return year;
        }

        private async Task<int> FillWaitingAsync()
        {
            var unreachable = new HashSet<int>();
            var filled = 0;

            foreach (var request in _requests.All().Where(r => r.Media == null || LacksPage(r)).OrderByDescending(r => r.Requested).ToList())
            {
                var instance = InstanceFor(request);

                if (instance == null || unreachable.Contains(instance.Id))
                {
                    continue;
                }

                if (await FillAsync(request, instance))
                {
                    filled++;
                }
                else
                {
                    unreachable.Add(instance.Id);
                }

                await Task.Delay(Delay);
            }

            return filled;
        }

        // In the library, and what its page there is called isn't known.
        private static bool LacksPage(Request request) =>
            request.Media.TitleSlug == null && request.ExternalItemId.HasValue && request.Status is RequestStatus.Approved or RequestStatus.Available;

        // False when the instance couldn't be reached: tried again next run. One request going wrong never stops the rest.
        private async Task<bool> FillAsync(Request request, BackendInstance instance)
        {
            MediaLookupResult item;

            try
            {
                item = await LookupAsync(instance, request);
            }
            catch (BackendException ex) when (ex.InnerException is HttpException { Response: not null })
            {
                // The instance answered, and has nothing for this title.
                item = null;
            }
            catch (BackendException ex)
            {
                _logger.Debug(ex, "Couldn't reach {0} to look up request {1}", instance.Name, request.Id);
                return false;
            }

            // Nothing found this time takes nothing away that was found before.
            if (item == null && request.Media != null)
            {
                return true;
            }

            try
            {
                request.Media = RequestMedia.From(item, DateTime.UtcNow);
                request.PosterUrl ??= item?.PosterUrl;
                request.Year ??= item?.Year;
                _requests.SetFields(request, r => r.Media, r => r.PosterUrl, r => r.Year);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't keep request {0}'s media; the next run tries again", request.Id);
            }

            return true;
        }

        // Its own instance when that's still on, else the type's first that is.
        private BackendInstance InstanceFor(Request request)
        {
            var implementation = request.ContentType == ContentType.Movie ? BackendImplementations.Radarr : BackendImplementations.Sonarr;
            var enabled = _backends.All().Where(b => b.Enable && b.Implementation == implementation).ToList();

            return enabled.FirstOrDefault(b => b.Id == request.BackendId) ?? enabled.FirstOrDefault();
        }

        private async Task<MediaLookupResult> LookupAsync(BackendInstance instance, Request request)
        {
            var isThere = request.ExternalItemId.HasValue && request.BackendId == instance.Id;
            var there = isThere ? await _backends.ApiFor(instance).GetItemAsync(instance.Config, request.ExternalItemId.Value) : null;

            return IsSame(there, request) ? there : await LookupByIdAsync(instance, request);
        }

        // An instance set up again can have another title by the same number.
        private static bool IsSame(MediaLookupResult item, Request request) =>
            item != null && (request.ContentType == ContentType.Movie ? item.TmdbId == request.TmdbId : item.TvdbId == request.TvdbId);

        // Movies by The Movie Database ID, shows by TheTVDB ID; a show known only by The Movie Database isn't looked up.
        private Task<MediaLookupResult> LookupByIdAsync(BackendInstance instance, Request request) => request.ContentType switch
        {
            ContentType.Movie when request.TmdbId.HasValue => _radarr.LookupTmdbAsync(instance.Config, request.TmdbId.Value),
            ContentType.Tv when request.TvdbId.HasValue => _sonarr.LookupTvdbAsync(instance.Config, request.TvdbId.Value),
            _ => Task.FromResult<MediaLookupResult>(null)
        };
    }
}
