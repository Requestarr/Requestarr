using System;
using System.Net;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // Where an admin opens a request's title: its page in Sonarr or Radarr, and on The Movie Database or TheTVDB.
    public static class TitleLinks
    {
        // The instance's page for the title, while it's in that library and a browser can reach the instance: its
        // browser address when set, else its own address unless that's a bare container name (http://sonarr:8989).
        public static string Open(BackendInstance instance, Request request)
        {
            var root = instance == null ? null : BrowserRoot(instance.Config);

            if (root == null || request.ExternalItemId == null || request.Status is RequestStatus.NotInLibrary or RequestStatus.Removed)
            {
                return null;
            }

            var slug = request.Media?.TitleSlug;

            return instance.Implementation switch
            {
                BackendImplementations.Sonarr when slug != null => $"{root}/series/{Uri.EscapeDataString(slug)}",
                BackendImplementations.Radarr when slug != null || request.TmdbId.HasValue => $"{root}/movie/{Uri.EscapeDataString(slug ?? request.TmdbId.ToString())}",
                _ => null
            };
        }

        public static string BrowserRoot(BackendSettings settings)
        {
            var hasBrowserUrl = !string.IsNullOrWhiteSpace(settings?.BrowserUrl);
            var address = hasBrowserUrl ? settings.BrowserUrl : settings?.Url;

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
