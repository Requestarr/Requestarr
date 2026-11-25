using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // What the request lists filter by, in the order a request moves through them. The Requests page filters in the
    // browser by the same rules (frontend/src/Store/Actions/requestStates.js).
    public enum RequestState
    {
        Pending,
        Denied,
        Approved,
        NotAvailable,
        Downloading,
        Stuck,
        Available,
        Removed,
        NotInLibrary
    }

    public static class RequestStates
    {
        // No state or no type: every one.
        public static IEnumerable<PortalRequest> Filter(IEnumerable<PortalRequest> requests, RequestState? state, ContentType? type) =>
            requests.Where(r => (state == null || IsIn(r, state.Value)) && (type == null || r.ContentType == type));

        // NotAvailable: wanted and not all there yet, one that couldn't be added included. Stuck: what the feed calls stuck
        // or failed.
        private static bool IsIn(PortalRequest request, RequestState state) => state switch
        {
            RequestState.Pending => request.Status == RequestStatus.Pending,
            RequestState.Denied => request.Status == RequestStatus.Denied,
            RequestState.Approved => request.Status == RequestStatus.Approved,
            RequestState.NotAvailable => request.Status is RequestStatus.Approved or RequestStatus.Failed,
            RequestState.Downloading => request is { Status: RequestStatus.Approved, Progress: DownloadProgress.Downloading },
            RequestState.Stuck => request is { Status: RequestStatus.Approved, Progress: DownloadProgress.Stalled or DownloadProgress.Failed },
            RequestState.Available => request.Status == RequestStatus.Available,
            RequestState.Removed => request.Status == RequestStatus.Removed,
            RequestState.NotInLibrary => request.Status == RequestStatus.NotInLibrary,
            _ => false
        };
    }
}
