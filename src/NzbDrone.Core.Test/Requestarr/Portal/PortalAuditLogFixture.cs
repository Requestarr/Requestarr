using System;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Portal;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Portal
{
    // The audit log page and the header search, from a real database.
    [TestFixture]
    public class PortalAuditLogFixture : DbTest
    {
        private static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        private RequestarrUser _alice;
        private Request _dune;

        private PortalAuditLog Subject => Mocker.Resolve<PortalAuditLog>();

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
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());
            Mocker.SetConstant<IPortalQueries>(Mocker.Resolve<PortalQueries>());

            _alice = Db.Insert(new RequestarrUser { DisplayName = "Alice", Status = UserStatus.Active, Added = Start });
            _dune = Db.Insert(new Request { Title = "Dune", Year = 2021, ContentType = ContentType.Movie, OwnerUserId = _alice.Id, Status = RequestStatus.Pending, Requested = Start });

            Audited("request", _dune.Id, "request.create", $"discord:1 (alice)", 0, _alice.Id);
            Audited("request", _dune.Id, "request.approve", "web:owner", 24);
            Audited("user", _alice.Id, "user.quota", "web:owner", 48);
            Audited("settings", 0, "settings.change", "web:owner", 72);
        }

        private void Audited(string target, int id, string action, string actor, int hours, int? actorUserId = null) =>
            Db.Insert(new AuditEntry { TargetType = target, TargetId = id.ToString(), Action = action, Actor = actor, ActorUserId = actorUserId, Timestamp = Start.AddHours(hours) });

        private static PortalAuditFilter Filter(string member = null, string title = null, string action = null, DateTime? from = null, DateTime? to = null) =>
            new(member, title, action, from, to);

        [Test]
        public void everything_shows_newest_first_with_the_page_it_opens()
        {
            var entries = Subject.Find(Filter());

            entries.Select(e => e.What).Should().Equal("Setting changed", "Limit changed", "Approved", "Requested");
            entries[2].About.Should().Be("Dune (2021)");
            entries[2].RequestId.Should().Be(_dune.Id);
            entries[1].MemberId.Should().Be(_alice.Id);
        }

        [Test]
        public void filters_find_by_member_title_action_and_day()
        {
            Subject.Find(Filter(member: "alice")).Select(e => e.What).Should().Equal("Limit changed", "Requested");
            Subject.Find(Filter(title: "dune")).Select(e => e.What).Should().Equal("Approved", "Requested");
            Subject.Find(Filter(action: "approved")).Should().ContainSingle();
            Subject.Find(Filter(from: Start.AddHours(24), to: Start.AddHours(49))).Select(e => e.What).Should().Equal("Limit changed", "Approved");
        }

        [Test]
        public void the_header_search_finds_requests_by_title_and_members_by_name()
        {
            var found = Subject.Search("du");

            found.Requests.Should().ContainSingle().Which.Id.Should().Be(_dune.Id);
            found.Members.Should().BeEmpty();
            Subject.Search("ali").Members.Should().ContainSingle().Which.Name.Should().Be("Alice");
        }

        [Test]
        public void a_member_is_found_by_their_discord_id_and_by_a_name_they_had_before()
        {
            Db.Insert(new UserIdentity { UserId = _alice.Id, Provider = UserIdentity.DiscordProvider, ExternalId = "1", Username = "alice", Linked = Start });
            Audited("user", _alice.Id, MemberNameService.RenameAction, "discord:1 (alice)", 96, _alice.Id);
            var rename = Mocker.Resolve<IAuditRepository>().All().Last();
            rename.Detail = "from Ally to Alice; Discord username from @ally to @alice";
            Mocker.Resolve<IAuditRepository>().Update(rename);

            Subject.Search("1").Members.Should().ContainSingle().Which.DiscordId.Should().Be("1");
            Subject.Search("ally").Members.Should().ContainSingle().Which.Name.Should().Be("Alice", "a former name finds them as they're called now");
            Subject.Search("12").Members.Should().BeEmpty("a Discord ID is matched whole");
        }

        [Test]
        public void an_entry_that_recorded_only_a_discord_account_shows_that_members_name_now()
        {
            Db.Insert(new UserIdentity { UserId = _alice.Id, Provider = UserIdentity.DiscordProvider, ExternalId = "1", Username = "alice", Linked = Start });
            Audited("settings", 0, "qa.pass", "discord:1 (ally)", 120);

            Subject.Find(Filter())[0].Who.Should().Be("Alice");
        }
    }
}
