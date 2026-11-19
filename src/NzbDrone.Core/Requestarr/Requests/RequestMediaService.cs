using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    public interface IRequestMediaService
    {
        // Looks up what each request without it is, in the background (a start, an import).
        void FillInBackground();

        // The same, now; the number looked up.
        Task<int> FillAsync();
    }

    // Requests made before media was kept, and imported ones, get it here: looked up once in a Sonarr or Radarr (whose
    // own lookups ask The Movie Database and TheTVDB), one at a time and a moment apart, then kept with the request. A
    // title nothing finds is marked looked up, so it's never asked again; one whose Sonarr or Radarr can't be reached
    // is tried again on the next run.
    public class RequestMediaService : IRequestMediaService, IHandle<ApplicationStartedEvent>
    {
        public static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(500);

        // After a start, the instances are in place and checked first.
        public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(30);

        private readonly SemaphoreSlim _running = new(1, 1);
        private readonly IRequestRepository _requests;
        private readonly IBackendService _backends;
        private readonly IRadarrApi _radarr;
        private readonly ISonarrApi _sonarr;
        private readonly Logger _logger;

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
            FillInBackground();
        });

        public void FillInBackground() => _ = Task.Run(async () =>
        {
            try
            {
                await FillAsync();
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Looking up requests' media stopped; the next run carries on");
            }
        });

        public async Task<int> FillAsync()
        {
            if (!await _running.WaitAsync(0))
            {
                return 0;
            }

            try
            {
                var filled = 0;

                foreach (var request in _requests.All().Where(r => r.Media == null).OrderByDescending(r => r.Requested).ToList())
                {
                    filled += await FillAsync(request) ? 1 : 0;
                    await Task.Delay(Delay);
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

        // False when no Sonarr or Radarr could be asked: tried again next time.
        private async Task<bool> FillAsync(Request request)
        {
            var instance = InstanceFor(request);

            if (instance == null)
            {
                return false;
            }

            MediaLookupResult item;

            try
            {
                item = await LookupAsync(instance, request);
            }
            catch (BackendException ex)
            {
                _logger.Debug(ex, "Couldn't look up request {0} in {1}", request.Id, instance.Name);
                return false;
            }

            request.Media = RequestMedia.From(item, DateTime.UtcNow);
            request.PosterUrl ??= item?.PosterUrl;
            _requests.SetFields(request, r => r.Media, r => r.PosterUrl);

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
            ContentType.Movie when request.TmdbId.HasValue => _radarr.LookupTmdbAsync(instance.Settings, request.TmdbId.Value),
            ContentType.Tv when request.TvdbId.HasValue => _sonarr.LookupTvdbAsync(instance.Settings, request.TvdbId.Value),
            _ => Task.FromResult<MediaLookupResult>(null)
        };
    }
}
