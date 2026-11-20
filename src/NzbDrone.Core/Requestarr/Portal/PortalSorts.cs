using System;
using System.Collections.Generic;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Portal
{
    // The columns each paged portal list sorts by on the server, by the names the web app's tables give them. Status and
    // type sort by the words the web app shows for them, as its own tables do (words: a phrase's translation).
    public class PortalSorts
    {
        private readonly Func<string, string> _words;

        public PortalSorts(Func<string, string> words)
        {
            _words = words;
            Requests = new Dictionary<string, Func<PortalRequest, object>>
            {
                ["title"] = r => r.Title,
                ["type"] = r => _words($"Portal{r.ContentType}"),
                ["status"] = StatusWords,
                ["requester"] = r => r.Requester.Name,
                ["followers"] = r => r.Followers.Count,
                ["instance"] = r => r.Instance,
                ["requested"] = r => r.Requested
            };
            Members = new Dictionary<string, Func<PortalMember, object>>
            {
                ["name"] = m => m.Name,
                ["status"] = m => _words($"PortalMember{m.Status}"),
                ["requests"] = m => m.Requests,
                ["active"] = m => m.Active,
                ["pending"] = m => m.Pending,
                ["following"] = m => m.Following,
                ["byType"] = m => m.Requests,
                ["firstRequest"] = m => m.FirstRequest,
                ["lastRequest"] = m => m.LastRequest,
                ["added"] = m => m.Added
            };
        }

        public IReadOnlyDictionary<string, Func<PortalRequest, object>> Requests { get; }

        public IReadOnlyDictionary<string, Func<PortalMember, object>> Members { get; }

        public static readonly IReadOnlyDictionary<string, Func<PortalFollower, object>> Followers = new Dictionary<string, Func<PortalFollower, object>>
        {
            ["member"] = f => f.Person.Name,
            ["followed"] = f => f.Followed
        };

        // A list shown in its own order (newest or oldest first), which pages but doesn't sort.
        public static IReadOnlyDictionary<string, Func<T, object>> None<T>() => new Dictionary<string, Func<T, object>>();

        // "Approved", or while Sonarr or Radarr has it, "Approved: Downloading", as the status column reads.
        private object StatusWords(PortalRequest request)
        {
            var status = _words($"PortalStatus{request.Status}");

            return request.Status == RequestStatus.Approved && request.Progress != DownloadProgress.None
                ? $"{status}: {_words($"PortalProgress{request.Progress}")}"
                : status;
        }
    }
}
