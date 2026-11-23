using System;
using System.Net;
using System.Net.Sockets;
using NzbDrone.Common.Http;

namespace Requestarr.Core.Settings
{
    // Why Requestarr couldn't reach a Sonarr or Radarr from the form, in the admin's words, so a choice list that
    // can't load or a Test that fails says what to change. App: the instance's type, as the form names it.
    public static class ConnectionProblems
    {
        public const string NoAddress = "Fill in the address first.";

        public static string NoApiKey(string app) => $"Paste {app}'s API key first, from {app}'s Settings, then General.";

        public static string Explain(string app, string url, Exception ex)
        {
            var host = Uri.TryCreate(url?.Trim(), UriKind.Absolute, out var uri) ? uri.Host : url;

            if (Find<HttpException>(ex) is { } http && http.Response?.StatusCode == HttpStatusCode.Unauthorized)
            {
                return $"{app} didn't accept this API key. Copy it again from {app}'s Settings, then General.";
            }

            if (IsLocal(host))
            {
                return $"Couldn't reach {app} at {host}: inside Requestarr's container, {host} means Requestarr itself. Use your server's IP address instead.";
            }

            if (Find<SocketException>(ex) is { SocketErrorCode: SocketError.HostNotFound or SocketError.NoData or SocketError.TryAgain })
            {
                return $"Couldn't find a server named {host}. Inside Requestarr's container, a computer's network name may not work; use its IP address instead.";
            }

            return $"Couldn't reach {app} at this address. Check the address and port: {ex.Message}";
        }

        public static bool IsLocal(string host) =>
            string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) || (IPAddress.TryParse(host?.Trim('[', ']'), out var address) && IPAddress.IsLoopback(address));

        private static T Find<T>(Exception ex)
            where T : Exception
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (current is T found)
                {
                    return found;
                }
            }

            return null;
        }
    }
}
