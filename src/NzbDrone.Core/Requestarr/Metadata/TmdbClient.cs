using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NLog;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Users;

namespace Requestarr.Core.Metadata
{
    public record TmdbDetails(IReadOnlyList<string> Cast, int? EpisodeCount);

    public interface ITmdbClient
    {
        Task<TmdbDetails> GetAsync(ContentType type, int tmdbId, CancellationToken cancellationToken);

        // The year a title first came out, by its The Movie Database ID (or a show's TheTVDB ID); null when The Movie
        // Database has no date for it or can't be asked.
        Task<int?> YearAsync(ContentType type, int? tmdbId, int? tvdbId, CancellationToken cancellationToken);
    }

    // Cast (and TV episode counts) for one title per call. Uses its own HttpClient rather than the
    // Servarr one so request URLs, which may carry the key, never reach the logs.
    public class TmdbClient : ITmdbClient
    {
        public const int CastCount = 3;

        private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };
        private static readonly Uri BaseUri = new("https://api.themoviedb.org/3/");

        private readonly ITmdbSettingsStore _settings;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public TmdbClient(ITmdbSettingsStore settings, IEventAggregator events, Logger logger)
        {
            _settings = settings;
            _events = events;
            _logger = logger;
        }

        // Swapped in tests; the live transport is the shared HttpClient.
        public Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> Transport { get; set; } = (request, token) => Http.SendAsync(request, token);

        public async Task<TmdbDetails> GetAsync(ContentType type, int tmdbId, CancellationToken cancellationToken)
        {
            if (tmdbId <= 0 || (type != ContentType.Movie && type != ContentType.Tv))
            {
                return null;
            }

            var json = await ReadAsync(DetailsPath(type, tmdbId), $"{type} {tmdbId}", cancellationToken);

            if (json == null)
            {
                return null;
            }

            var credits = type == ContentType.Movie ? json["credits"] : json["aggregate_credits"];
            var cast = credits?["cast"]?
                .OrderBy(c => (int?)c["order"] ?? int.MaxValue)
                .Select(c => (string)c["name"])
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Take(CastCount)
                .ToList() ?? new List<string>();
            var episodes = type == ContentType.Tv && (int?)json["number_of_episodes"] is > 0 ? (int?)json["number_of_episodes"] : null;

            return new TmdbDetails(cast, episodes);
        }

        public async Task<int?> YearAsync(ContentType type, int? tmdbId, int? tvdbId, CancellationToken cancellationToken)
        {
            if (type == ContentType.Tv && tmdbId is not > 0 && tvdbId is > 0)
            {
                var found = await ReadAsync($"find/{tvdbId}?external_source=tvdb_id", $"TheTVDB {tvdbId}", cancellationToken);
                return YearOf((string)found?["tv_results"]?.FirstOrDefault()?["first_air_date"]);
            }

            if (tmdbId is not > 0 || (type != ContentType.Movie && type != ContentType.Tv))
            {
                return null;
            }

            var details = await ReadAsync(type == ContentType.Movie ? $"movie/{tmdbId}" : $"tv/{tmdbId}", $"{type} {tmdbId}", cancellationToken);
            return YearOf((string)details?[type == ContentType.Movie ? "release_date" : "first_air_date"]);
        }

        // A date as The Movie Database writes it (2026-12-07): its year; null when it has none.
        private static int? YearOf(string date) => date?.Length >= 4 && int.TryParse(date[..4], out var year) && year > 0 ? year : null;

        private static string DetailsPath(ContentType type, int tmdbId) =>
            type == ContentType.Movie ? $"movie/{tmdbId}?append_to_response=credits" : $"tv/{tmdbId}?append_to_response=aggregate_credits";

        // One call, its answer as JSON; null without a key, or when it fails (logged by what it asked for).
        private async Task<JObject> ReadAsync(string path, string what, CancellationToken cancellationToken)
        {
            var key = _settings.ApiKey;

            if (string.IsNullOrWhiteSpace(key))
            {
                return null;
            }

            using var request = Build(path, key);

            try
            {
                using var response = await Transport(request, cancellationToken);

                ReportKey(response.StatusCode);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn("The Movie Database lookup for {0} returned HTTP {1}", what, (int)response.StatusCode);
                    return null;
                }

                return JObject.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
            }
            catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
            {
                // Only the exception type: messages and URLs could carry the key.
                _logger.Warn("The Movie Database lookup for {0} failed ({1})", what, ex.GetType().Name);
                return null;
            }
        }

        // A refused key (401) or one over its rate limit (429) is the key's problem; an answer (2xx) or a title it
        // doesn't know (404) says the key works. Anything else, such as The Movie Database being down, says nothing.
        private void ReportKey(HttpStatusCode status)
        {
            var works = (int)status is >= 200 and < 300 || status == HttpStatusCode.NotFound;
            var problem = status is HttpStatusCode.Unauthorized or HttpStatusCode.TooManyRequests ? $"HTTP {(int)status}" : null;

            if ((works || problem != null) && _settings.Report(problem))
            {
                _events.PublishEvent(new TmdbKeyChangedEvent());
            }
        }

        // A v4 read access token (a JWT) goes in the Authorization header; a v3 key in the query.
        public static HttpRequestMessage Build(ContentType type, int tmdbId, string key) => Build(DetailsPath(type, tmdbId), key);

        private static HttpRequestMessage Build(string path, string key)
        {
            var bearer = IsReadAccessToken(key);
            var query = path.Contains('?') ? "&" : "?";
            var request = new HttpRequestMessage(HttpMethod.Get, new Uri(BaseUri, bearer ? path : $"{path}{query}api_key={Uri.EscapeDataString(key)}"));

            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            if (bearer)
            {
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            }

            return request;
        }

        public static bool IsReadAccessToken(string key) => key.StartsWith("eyJ", StringComparison.Ordinal) && key.Count(c => c == '.') == 2;
    }
}
