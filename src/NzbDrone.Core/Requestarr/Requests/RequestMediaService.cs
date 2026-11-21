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
    // that can't be reached is left for the next run, and skipped for the rest of this one. It runs after a start,
    // after an import, and every few hours, which also fills in a request whose media another change wrote over.
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
        private readonly Logger _logger;

        // Asked while a run was going: that run reads the requests again when it's done.
        private int _again;

        public RequestMediaService(IRequestRepository requests, IBackendService backends, IRadarrApi radarr, ISonarrApi sonarr, Logger logger)
        {
            _requests = requests;
            _backends = backends;
            _radarr = radarr;
            _sonarr = sonarr;
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

                var years = FillYears();

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

        // Requests without a year (older Requestrr replies imported without one) that know when their title came out: the
        // year it first did, so they're shown as "Smart House (1999)".
        private int FillYears()
        {
            var filled = 0;

            foreach (var request in _requests.All().Where(r => r.Year == null && r.Media?.FirstYear() != null).ToList())
            {
                request.Year = request.Media.FirstYear();
                _requests.SetFields(request, r => r.Year);
                filled++;
            }

            return filled;
        }

        private async Task<int> FillWaitingAsync()
        {
            var unreachable = new HashSet<int>();
            var filled = 0;

            foreach (var request in _requests.All().Where(r => r.Media == null).OrderByDescending(r => r.Requested).ToList())
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

        // Movies by The Movie Database ID, shows by TheTVDB ID; a show known only by The Movie Database isn't looked up.
        private Task<MediaLookupResult> LookupAsync(BackendInstance instance, Request request) => request.ContentType switch
        {
            ContentType.Movie when request.TmdbId.HasValue => _radarr.LookupTmdbAsync(instance.Config, request.TmdbId.Value),
            ContentType.Tv when request.TvdbId.HasValue => _sonarr.LookupTvdbAsync(instance.Config, request.TvdbId.Value),
            _ => Task.FromResult<MediaLookupResult>(null)
        };
    }
}
