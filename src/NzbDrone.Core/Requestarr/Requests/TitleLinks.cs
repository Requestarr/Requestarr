using System;
using System.Net;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // Why a title can't be opened in its Sonarr or Radarr.
    public enum TitleLinkProblem
    {
        NotAdded,
        Removed,
        NoAddress,
        NoPage
    }

    // The title's page in its Sonarr or Radarr, or why there isn't one.
    public record TitleLink(string Url, TitleLinkProblem? Problem);

    // Where an admin opens a request's title: its page in Sonarr or Radarr, and on The Movie Database or TheTVDB.
    public static class TitleLinks
    {
        // The instance's page for the title, while it's in that library and a browser can reach the instance: its
        // browser address when set, else its own address unless that's a bare container name (http://sonarr:8989).
        public static string Open(BackendInstance instance, Request request) => Find(instance, request).Url;

        public static TitleLink Find(BackendInstance instance, Request request)
        {
            if (request.Status == RequestStatus.Removed)
            {
                return new TitleLink(null, TitleLinkProblem.Removed);
            }

            if (instance == null || request.ExternalItemId == null || request.Status == RequestStatus.NotInLibrary)
            {
                return new TitleLink(null, TitleLinkProblem.NotAdded);
            }

            if (BrowserRoot(instance.Config) is not { } root)
            {
                return new TitleLink(null, TitleLinkProblem.NoAddress);
            }

            return PathOf(instance, request) is { } path ? new TitleLink(root + path, null) : new TitleLink(null, TitleLinkProblem.NoPage);
        }

        // The app's own route: Sonarr's by the series' slug, Radarr's by the movie's, which is its The Movie Database ID.
        private static string PathOf(BackendInstance instance, Request request)
        {
            var slug = request.Media?.TitleSlug;

            return instance.Implementation switch
            {
                BackendImplementations.Sonarr when slug != null => $"/series/{Uri.EscapeDataString(slug)}",
                BackendImplementations.Radarr when slug != null || request.TmdbId.HasValue => $"/movie/{Uri.EscapeDataString(slug ?? request.TmdbId.ToString())}",
                _ => null
            };
        }

        public static string BrowserRoot(BackendSettings settings) => BrowserRoot(settings?.BrowserUrl, settings?.Url);

        // The address given for browsers, else the app's own when a browser can reach it; null when neither will do.
        public static string BrowserRoot(string browserUrl, string ownUrl)
        {
            var hasBrowserUrl = !string.IsNullOrWhiteSpace(browserUrl);
            var address = hasBrowserUrl ? browserUrl : ownUrl;

            if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out var uri) || (!hasBrowserUrl && !Reachable(uri)))
            {
                return null;
            }

            // Its address up to the path (a URL base kept, any query left out), still escaped.
            return uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
        }

        // The title on The Movie Database (movies, and shows known only there) or TheTVDB.
        public static string Page(Request request) => request.ContentType switch
        {
            ContentType.Movie when request.TmdbId.HasValue => $"https://www.themoviedb.org/movie/{request.TmdbId}",
            ContentType.Tv when request.TvdbId.HasValue => $"https://www.thetvdb.com/?tab=series&id={request.TvdbId}",
            ContentType.Tv when request.TmdbId.HasValue => $"https://www.themoviedb.org/tv/{request.TmdbId}",
            _ => null
        };

        // A name a browser can look up: this machine, an IP address, or a name with a dot in it.
        private static bool Reachable(Uri uri) => uri.IsLoopback || IPAddress.TryParse(uri.Host, out _) || uri.Host.Contains('.');
    }
}
