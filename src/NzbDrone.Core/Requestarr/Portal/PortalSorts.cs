using System;
using System.Collections.Generic;

namespace Requestarr.Core.Portal
{
    // The columns each paged portal list sorts by on the server, by the names the web app's tables give them.
    public static class PortalSorts
    {
        public static readonly IReadOnlyDictionary<string, Func<PortalRequest, object>> Requests = new Dictionary<string, Func<PortalRequest, object>>
        {
            ["title"] = r => r.Title,
            ["type"] = r => r.ContentType.ToString(),
            ["status"] = r => r.Status.ToString(),
            ["requester"] = r => r.Requester.Name,
            ["followers"] = r => r.Followers.Count,
            ["instance"] = r => r.Instance,
            ["requested"] = r => r.Requested
        };

        public static readonly IReadOnlyDictionary<string, Func<PortalFollower, object>> Followers = new Dictionary<string, Func<PortalFollower, object>>
        {
            ["member"] = f => f.Person.Name,
            ["followed"] = f => f.Followed
        };

        public static readonly IReadOnlyDictionary<string, Func<PortalMember, object>> Members = new Dictionary<string, Func<PortalMember, object>>
        {
            ["name"] = m => m.Name,
            ["status"] = m => m.Status.ToString(),
            ["requests"] = m => m.Requests,
            ["active"] = m => m.Active,
            ["pending"] = m => m.Pending,
            ["following"] = m => m.Following,
            ["byType"] = m => m.Requests,
            ["firstRequest"] = m => m.FirstRequest,
            ["lastRequest"] = m => m.LastRequest,
            ["added"] = m => m.Added
        };

        // A list shown in its own order (newest or oldest first), which pages but doesn't sort.
        public static IReadOnlyDictionary<string, Func<T, object>> None<T>() => new Dictionary<string, Func<T, object>>();
    }
}
