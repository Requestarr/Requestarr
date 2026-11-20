using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
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
            Mocker.SetConstant<IRemovalFlagRepository>(Mocker.Resolve<RemovalFlagRepository>());
            Mocker.SetConstant<IMediaServerAccountRepository>(Mocker.Resolve<MediaServerAccountRepository>());
            Mocker.SetConstant<IMediaServerAccessRequestRepository>(Mocker.Resolve<MediaServerAccessRequestRepository>());
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());
            Mocker.GetMock<IConfigService>().SetupAllProperties();
            Mocker.SetConstant<IAuditSettings>(Mocker.Resolve<AuditSettings>());

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

        [Test]
        public void someone_imported_is_first_seen_with_their_first_request_and_every_member_shows_picture_handle_dates_and_types()
        {
            var imported = Db.Insert(new RequestarrUser { DisplayName = "Chaz", Status = UserStatus.ImportedElsewhere, Added = Start.AddDays(30) });
            Db.Insert(new UserIdentity { UserId = imported.Id, Provider = UserIdentity.DiscordProvider, ExternalId = "42", Username = "chaz", AvatarUrl = "https://cdn.discordapp.com/avatars/42/a.png" });
            Requested("Heat", imported, RequestStatus.NotInLibrary, 3);
            Requested("Alien", imported, RequestStatus.NotInLibrary, 9);
            var show = Requested("Severance", imported, RequestStatus.NotInLibrary, 6);
            show.ContentType = ContentType.Tv;
            Db.Update(show);

            var member = Subject.Members().Single(m => m.Id == imported.Id);

            Db.Insert(new RequestSubscriber { RequestId = Requested("Earlier", _alice, RequestStatus.Approved, 1).Id, UserId = imported.Id, Added = Start.AddHours(1) });
            member = Subject.Members().Single(m => m.Id == imported.Id);

            member.Added.Should().Be(Start.AddHours(3), "their earliest imported request: not the import, nor an earlier follow");
            (member.FirstRequest, member.LastRequest).Should().Be((Start.AddHours(3), Start.AddHours(9)));
            member.ByType.Should().Equal(new PortalTypeCount(ContentType.Movie, 2), new PortalTypeCount(ContentType.Tv, 1));
            (member.Avatar, member.DiscordName).Should().Be(("https://cdn.discordapp.com/avatars/42/a.png", "chaz"));
            Subject.Members().Single(m => m.Id == _alice.Id).Added.Should().Be(Start, "a member keeps the day they were added");
            Subject.Requests().First(r => r.Requester.Id == imported.Id).Requester
                .Should().Be(new PortalPerson(imported.Id, "Chaz", "https://cdn.discordapp.com/avatars/42/a.png", "chaz"));

            Mocker.GetMock<IAdminQueue>().Setup(q => q.Items()).Returns(new List<QueueItem> { new($"r{show.Id}", Start) });
            var waiting = Subject.Queue().Single();
            (waiting.WhoAvatar, waiting.WhoHandle).Should().Be(("https://cdn.discordapp.com/avatars/42/a.png", "chaz"));
        }

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
            row.Requester.Should().Be(new PortalPerson(_alice.Id, "Alice", null, null));
            row.Followers.Should().Equal(new PortalPerson(_bob.Id, "Bob", null, null));
            row.Instance.Should().Be("Radarr 4K");
        }

        [Test]
        public void a_requests_timeline_is_in_words_oldest_first_with_its_removals()
        {
            var dune = Requested("Dune", _alice, RequestStatus.Available, 1);
            var removal = Db.Insert(new RemovalProposal { RequestId = dune.Id, Title = "Dune", Status = RemovalStatus.Voting, Created = Start.AddHours(9), InitiatedByName = "Bob" });
            Audited("request", dune.Id, "request.approve", "discord:111 (Carol)", 3, "looks good");
            Db.Insert(new AuditEntry { TargetType = "request", TargetId = dune.Id.ToString(), Action = "request.subscribe", Actor = "discord:222 (user-222)", ActorUserId = _bob.Id, Timestamp = Start.AddHours(4) });
            Audited("request", dune.Id, "request.fulfilled", "system", 6);
            Audited("removal", removal.Id, "removal.start", "discord:222 (Bob)", 9);
            Mocker.GetMock<IRemovalService>().Setup(s => s.LatestFor(dune.Id)).Returns(removal);

            var detail = Subject.Request(dune.Id);

            detail.Timeline.Should().Equal(
                new PortalEvent(Start.AddHours(1), "Requested", "Alice", null),
                new PortalEvent(Start.AddHours(3), "Approved", "Carol", "looks good"),
                new PortalEvent(Start.AddHours(4), "Followed", "Bob", null),
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
        public void a_waiting_removal_names_and_opens_its_requester_and_a_limit_without_a_window_counts_7_days()
        {
            var dune = Requested("Dune", _alice, RequestStatus.Available, 1);
            var removal = Db.Insert(new RemovalProposal { RequestId = dune.Id, Title = "Dune", Year = 2021, Status = RemovalStatus.Declined, Created = Start, RequesterUserId = _alice.Id, InitiatedByUserId = _bob.Id, InitiatedByName = "Bob" });
            Db.Insert(new UserMediaAccess { UserId = _alice.Id, ContentType = ContentType.Movie, State = AccessState.AutoApprove, QuotaLimit = 2 });
            Mocker.GetMock<IAdminQueue>().Setup(q => q.Items()).Returns(new List<QueueItem> { new($"x{removal.Id}", Start) });

            Subject.Queue().Single().Should().Be(new PortalQueueItem($"x{removal.Id}", "removal", Start, "Dune (2021)", "Alice", dune.Id, _alice.Id));
            Subject.Members().Single(m => m.Id == _alice.Id).Access.Single().QuotaDays.Should().Be(7);
        }

        [Test]
        public void an_unknown_request_or_member_is_not_found()
        {
            Subject.Request(999).Should().BeNull();
            Subject.Member(999).Should().BeNull();
        }

        [Test]
        public void a_page_that_began_before_a_trims_cutoff_says_its_history_was_trimmed()
        {
            var now = DateTime.UtcNow;
            var old = Db.Insert(new Request { Title = "Alien", ContentType = ContentType.Movie, OwnerUserId = _alice.Id, Status = RequestStatus.Available, Requested = now.AddYears(-2) });
            var recent = Db.Insert(new Request { Title = "Dune", ContentType = ContentType.Movie, OwnerUserId = _alice.Id, Status = RequestStatus.Pending, Requested = now.AddDays(-1) });
            var carol = Db.Insert(new RequestarrUser { DisplayName = "Carol", Status = UserStatus.Active, Added = now.AddYears(-2) });
            var dave = Db.Insert(new RequestarrUser { DisplayName = "Dave", Status = UserStatus.Active, Added = now.AddDays(-1) });

            Subject.Request(old.Id).HistoryTrimmedAfter.Should().BeNull("nothing has been trimmed yet");

            Mocker.Resolve<AuditSettings>().Trimmed(now.AddYears(-1));

            Subject.Request(old.Id).HistoryTrimmedAfter.Should().Be(AuditRetention.OneYear);
            Subject.Member(carol.Id).HistoryTrimmedAfter.Should().Be(AuditRetention.OneYear);
            Subject.Request(recent.Id).HistoryTrimmedAfter.Should().BeNull();
            Subject.Member(dave.Id).HistoryTrimmedAfter.Should().BeNull();

            Mocker.GetMock<IConfigService>().Object.AuditLogKeepFor = AuditRetention.Forever;
            Subject.Request(old.Id).HistoryTrimmedAfter.Should().Be(AuditRetention.Forever, "what was trimmed before stays gone");
        }

        [Test]
        public void an_action_without_words_yet_shows_as_recorded()
        {
            PortalText.Action("request.something-new").Should().Be("request.something-new");
            PortalText.Actor("automation").Should().Be(PortalText.Requestarr);
        }

        [Test]
        public void open_removals_of_titles_nobody_requested_are_listed_on_their_own()
        {
            var dune = Requested("Dune", _alice, RequestStatus.Available, 1);
            Db.Insert(new RemovalProposal { RequestId = dune.Id, Title = "Dune", Year = 2021, Status = RemovalStatus.Voting, Created = Start });
            Db.Insert(new RemovalProposal { Title = "Heat", Year = 1995, Status = RemovalStatus.Voting, Created = Start });
            Db.Insert(new RemovalProposal { Title = "Alien", Year = 1979, Status = RemovalStatus.Kept, Created = Start });

            Subject.TitleRemovals().Should().ContainSingle().Which.Title.Should().Be("Heat (1995)");
        }

        [Test]
        public void a_request_offers_the_actions_its_feed_card_does()
        {
            var pending = Requested("Dune", _alice, RequestStatus.Pending, 1);
            var failed = Requested("Arrival", _alice, RequestStatus.Failed, 2);

            Subject.Request(pending.Id).Actions.Should().Equal("approve", "deny", "available", "cancel");
            Subject.Request(failed.Id).Actions.Should().Equal("available", "cancel", "retry");
        }

        [TestCase("request.create", "Tv Severance (2022) on Sonarr (test), seasons seasons 1–2 only", "Severance (2022) on Sonarr (test), seasons 1–2 only")]
        [TestCase("request.progress", "None, season 1 available", "season 1 available")]
        [TestCase("user.access", "Tv: NeedsApproval -> AutoApprove", "TV: needs approval to approved automatically")]
        [TestCase("user.add", "discord 123, role 456, channel 789, access Movie=NeedsApproval, Tv=Off", "access Movie: needs approval, TV: Off")]
        [TestCase("removal.force", "Dune (2021) forced by Owner at 2026-11-17 01:28:00Z (was Voting, 1 keep flag(s))", "Dune (2021) forced by Owner (was Voting, 1 Keep it vote)")]
        [TestCase("request.deny", "None, E=mc2 -> later", "None, E=mc2 -> later")]
        [TestCase("removal.start", "Movie 43 (2013) on Radarr (test), by the admin", "Movie 43 (2013) on Radarr (test), by the admin")]
        public void recorded_details_read_in_words_and_only_their_own_actions_are_reworded(string action, string recorded, string shown)
        {
            PortalText.Detail(action, recorded).Should().Be(shown);
        }

        [Test]
        public void a_progress_with_nothing_to_report_has_no_detail_and_a_sent_request_names_its_instance()
        {
            PortalText.Detail("request.progress", "None").Should().BeNull();

            var sent = PortalText.Event(new AuditEntry { Action = "request.send", Actor = "system", Detail = "Sonarr (test) item 12, profile HD-1080p, root /data/tv, type standard, tag 3" }, new Dictionary<int, string>(), new Dictionary<string, string>());

            sent.What.Should().Be("Sent to Sonarr (test)");
            sent.Detail.Should().Be("quality profile HD-1080p, root folder /data/tv, type standard");
        }
    }
}
