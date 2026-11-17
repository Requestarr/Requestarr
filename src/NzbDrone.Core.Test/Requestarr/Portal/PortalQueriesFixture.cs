using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Portal;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Portal
{
    // The admin portal's read views, from a real database: who asked and followed, members' access and counts,
    // timelines in words, and the admin queue's items as the portal shows them.
    [TestFixture]
    public class PortalQueriesFixture : DbTest
    {
        private static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        private RequestarrUser _alice;
        private RequestarrUser _bob;
        private BackendInstance _radarr;

        private PortalQueries Subject => Mocker.Resolve<PortalQueries>();

        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            Mocker.SetConstant<IUserIdentityRepository>(Mocker.Resolve<UserIdentityRepository>());
            Mocker.SetConstant<IUserMediaAccessRepository>(Mocker.Resolve<UserMediaAccessRepository>());
            Mocker.SetConstant<IBackendRepository>(Mocker.Resolve<BackendRepository>());
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IRemovalProposalRepository>(Mocker.Resolve<RemovalProposalRepository>());
            Mocker.SetConstant<IMediaServerAccountRepository>(Mocker.Resolve<MediaServerAccountRepository>());
            Mocker.SetConstant<IMediaServerAccessRequestRepository>(Mocker.Resolve<MediaServerAccessRequestRepository>());
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());

            _alice = Db.Insert(new RequestarrUser { DisplayName = "Alice", Status = UserStatus.Active, Added = Start });
            _bob = Db.Insert(new RequestarrUser { DisplayName = "Bob", Status = UserStatus.Suspended, Added = Start });
            _radarr = Db.Insert(new BackendInstance { Name = "Radarr 4K", Implementation = BackendImplementations.Radarr, Added = Start });
        }

        private Request Requested(string title, RequestarrUser owner, RequestStatus status, int hours) =>
            Db.Insert(new Request
            {
                Title = title,
                Year = 2021,
                ContentType = ContentType.Movie,
                OwnerUserId = owner.Id,
                Status = status,
                BackendId = _radarr.Id,
                Requested = Start.AddHours(hours)
            });

        private void Followed(Request request, RequestarrUser user) =>
            Db.Insert(new RequestSubscriber { RequestId = request.Id, UserId = user.Id, Added = Start.AddHours(5) });

        private void Audited(string target, int id, string action, string actor, int hours, string detail = null) =>
            Db.Insert(new AuditEntry { TargetType = target, TargetId = id.ToString(), Action = action, Actor = actor, Timestamp = Start.AddHours(hours), Detail = detail });

        [Test]
        public void requests_name_their_requester_followers_and_instance_newest_first()
        {
            var dune = Requested("Dune", _alice, RequestStatus.Approved, 1);
            Requested("Arrival", _bob, RequestStatus.Pending, 2);
            Followed(dune, _bob);

            var requests = Subject.Requests();

            requests.Select(r => r.Title).Should().Equal("Arrival", "Dune");
            var row = requests.Single(r => r.Title == "Dune");
            row.Requester.Should().Be(new PortalPerson(_alice.Id, "Alice"));
            row.Followers.Should().Equal(new PortalPerson(_bob.Id, "Bob"));
            row.Instance.Should().Be("Radarr 4K");
        }

        [Test]
        public void a_requests_timeline_is_in_words_oldest_first_with_its_removals()
        {
            var dune = Requested("Dune", _alice, RequestStatus.Available, 1);
            var removal = Db.Insert(new RemovalProposal { RequestId = dune.Id, Title = "Dune", Status = RemovalStatus.Voting, Created = Start.AddHours(9), InitiatedByName = "Bob" });
            Audited("request", dune.Id, "request.approve", "discord:111 (Carol)", 3, "looks good");
            Audited("request", dune.Id, "request.fulfilled", "system", 6);
            Audited("removal", removal.Id, "removal.start", "discord:222 (Bob)", 9);
            Mocker.GetMock<IRemovalService>().Setup(s => s.LatestFor(dune.Id)).Returns(removal);

            var detail = Subject.Request(dune.Id);

            detail.Timeline.Should().Equal(
                new PortalEvent(Start.AddHours(1), "Requested", "Alice", null),
                new PortalEvent(Start.AddHours(3), "Approved", "Carol", "looks good"),
                new PortalEvent(Start.AddHours(6), "Available", PortalText.Requestarr, null),
                new PortalEvent(Start.AddHours(9), "Removal started", "Bob", null));
            detail.LatestRemoval.StartedBy.Should().Be("Bob");
        }

        [Test]
        public void members_show_their_access_limits_and_counts()
        {
            Requested("Dune", _alice, RequestStatus.Approved, 1);
            Requested("Arrival", _alice, RequestStatus.Pending, 2);
            Requested("Tenet", _alice, RequestStatus.Denied, 3);
            Followed(Requested("Heat", _bob, RequestStatus.Available, 4), _alice);
            Db.Insert(new UserMediaAccess { UserId = _alice.Id, ContentType = ContentType.Movie, State = AccessState.NeedsApproval, QuotaLimit = 3, QuotaWindow = TimeSpan.FromDays(7), BackendId = _radarr.Id });
            Db.Insert(new UserIdentity { UserId = _alice.Id, Provider = UserIdentity.DiscordProvider, ExternalId = "111", Username = "alice.d" });

            var alice = Subject.Members().Single(m => m.Id == _alice.Id);

            alice.Should().BeEquivalentTo(new { Name = "Alice", DiscordName = "alice.d", Requests = 3, Active = 2, Pending = 1, Following = 1 });
            alice.Access.Should().Equal(new PortalAccess(ContentType.Movie, AccessState.NeedsApproval, 3, 7, "Radarr 4K"));
        }

        [Test]
        public void a_member_shows_their_requests_what_they_follow_removals_media_server_and_history()
        {
            var dune = Requested("Dune", _alice, RequestStatus.Approved, 1);
            Followed(Requested("Heat", _bob, RequestStatus.Available, 2), _alice);
            Db.Insert(new RemovalProposal { RequestId = dune.Id, Title = "Dune", Status = RemovalStatus.Kept, Created = Start.AddHours(3), RequesterUserId = _alice.Id });
            Db.Insert(new MediaServerAccount { UserId = _alice.Id, Provider = MediaServerProvider.Plex, Username = "alice_plex", Added = Start });
            Audited("user", _alice.Id, "user.add", "discord:111 (Carol)", 0);
            Audited("user", _alice.Id, "user.quota", "discord:111 (Carol)", 1, "3 every 7 days");

            var detail = Subject.Member(_alice.Id);

            detail.Requests.Select(r => r.Title).Should().Equal("Dune");
            detail.Following.Select(r => r.Title).Should().Equal("Heat");
            detail.Removals.Select(r => r.Status).Should().Equal(RemovalStatus.Kept);
            detail.MediaServer.Should().Be("Plex: alice_plex");
            detail.History.Select(e => e.What).Should().Equal("Added", "Limit changed");
        }

        [Test]
        public void the_queue_says_what_each_item_is_and_leaves_out_ones_gone_since()
        {
            var arrival = Requested("Arrival", _bob, RequestStatus.Pending, 2);
            Mocker.GetMock<IAdminQueue>().Setup(q => q.Items()).Returns(new List<QueueItem>
            {
                new($"r{arrival.Id}", Start.AddHours(2)),
                new($"i{_radarr.Id}", Start.AddHours(3)),
                new("r999", Start.AddHours(4))
            });

            var queue = Subject.Queue();

            queue.Should().Equal(
                new PortalQueueItem($"r{arrival.Id}", "request", Start.AddHours(2), "Arrival (2021)", "Bob", arrival.Id, _bob.Id),
                new PortalQueueItem($"i{_radarr.Id}", "instanceDown", Start.AddHours(3), "Radarr 4K", null, null, null));
        }

        [Test]
        public void an_unknown_request_or_member_is_not_found()
        {
            Subject.Request(999).Should().BeNull();
            Subject.Member(999).Should().BeNull();
        }

        [Test]
        public void an_action_without_words_yet_shows_as_recorded()
        {
            PortalText.Action("request.something-new").Should().Be("request.something-new");
            PortalText.Actor("claude-code").Should().Be(PortalText.Requestarr);
        }
    }
}
