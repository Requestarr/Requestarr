using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Portal;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Portal
{
    // A member's requests filtered by the Filter menu's states and types.
    [TestFixture]
    public class RequestStatesFixture
    {
        private static readonly List<PortalRequest> All = new()
        {
            Request("Waiting", RequestStatus.Pending),
            Request("Denied", RequestStatus.Denied),
            Request("Cancelled", RequestStatus.Cancelled),
            Request("Searching", RequestStatus.Approved),
            Request("Downloading", RequestStatus.Approved, DownloadProgress.Downloading),
            Request("Stalled", RequestStatus.Approved, DownloadProgress.Stalled),
            Request("Download failed", RequestStatus.Approved, DownloadProgress.Failed, ContentType.Movie),
            Request("Couldn't be added", RequestStatus.Failed),
            Request("There", RequestStatus.Available, DownloadProgress.Imported),
            Request("Gone", RequestStatus.Removed, DownloadProgress.Imported),
            Request("History", RequestStatus.NotInLibrary)
        };

        private static PortalRequest Request(string title, RequestStatus status, DownloadProgress progress = DownloadProgress.None, ContentType type = ContentType.Tv) =>
            new(1, title, null, type, status, progress, null, Array.Empty<PortalPerson>(), null, null, Array.Empty<int>(), DateTime.UtcNow, null, null, null, Array.Empty<string>(), null);

        [TestCase(RequestState.Pending, "Waiting")]
        [TestCase(RequestState.Denied, "Denied")]
        [TestCase(RequestState.Approved, "Searching", "Downloading", "Stalled", "Download failed")]
        [TestCase(RequestState.NotAvailable, "Searching", "Downloading", "Stalled", "Download failed", "Couldn't be added")]
        [TestCase(RequestState.Downloading, "Downloading")]
        [TestCase(RequestState.Stuck, "Stalled", "Download failed")]
        [TestCase(RequestState.Available, "There")]
        [TestCase(RequestState.Removed, "Gone")]
        [TestCase(RequestState.NotInLibrary, "History")]
        public void each_state_has_its_requests(RequestState state, params string[] titles)
        {
            RequestStates.Filter(All, state, null).Select(r => r.Title).Should().Equal(titles);
        }

        [Test]
        public void without_a_filter_there_is_every_request_and_a_type_goes_with_a_state()
        {
            RequestStates.Filter(All, null, null).Should().Equal(All);
            RequestStates.Filter(All, RequestState.Stuck, ContentType.Movie).Select(r => r.Title).Should().Equal("Download failed");
        }
    }
}
