using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Portal;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Portal
{
    // The portal's actions go through the services Discord's buttons use, naming the web admin as Discord shows them.
    [TestFixture]
    public class PortalActionsFixture : CoreTest<PortalActions>
    {
        private static readonly Actor Admin = Actor.Web("owner");

        [Test]
        public async Task approving_goes_through_the_request_service_with_the_admins_name()
        {
            Mocker.GetMock<IRequestService>().Setup(r => r.ApproveAsync(5, Admin, "owner (web app)")).ReturnsAsync(ServiceResult.Ok("Approved"));

            (await Subject.ApproveAsync(5, Admin)).Success.Should().BeTrue();
        }

        [Test]
        public async Task add_seasons_refuses_a_season_the_show_doesnt_have()
        {
            var request = new Request { Id = 9, Title = "Severance", Year = 2022, ContentType = ContentType.Tv, Seasons = new List<int> { 1 } };
            Mocker.GetMock<IRequestService>().Setup(r => r.Find(9)).Returns(request);
            Mocker.GetMock<IRequestService>().Setup(r => r.ItemForAsync(request)).ReturnsAsync(new MediaLookupResult { Seasons = new List<int> { 1, 2 } });

            var result = await Subject.AddSeasonsAsync(9, new SeasonChoice(false, new List<int> { 2, 99 }, false), Admin);

            result.Success.Should().BeFalse();
            result.Message.Should().Contain("99").And.Contain("It can add: 2");
            Mocker.GetMock<IRequestService>().Verify(r => r.AddSeasonsAsync(It.IsAny<int>(), It.IsAny<RequestarrUser>(), It.IsAny<bool>(), It.IsAny<Actor>(), It.IsAny<SeasonChoice>()), Times.Never());
        }

        [Test]
        public async Task a_removal_from_a_requests_page_starts_on_its_title_in_the_library()
        {
            var instance = new BackendInstance { Id = 3, Name = "Radarr" };
            var title = new LibraryTitle(42, "Dune", 2021, 1, null, true, new List<int>(), new List<int>(), null);
            Mocker.GetMock<IRemovalService>().Setup(r => r.TargetForRequestAsync(7)).ReturnsAsync(LookupOutcome<RemovalTarget>.Ok(new RemovalTarget(instance, title, null, null)));

            await Subject.StartRemovalAsync(7, new List<int>(), true, Admin);

            Mocker.GetMock<IRemovalService>().Verify(r => r.StartByAdminAsync(Admin, "owner (web app)", null, 3, 42, It.Is<IReadOnlyCollection<int>>(s => s.Count == 0), true), Times.Once());
        }

        [Test]
        public async Task a_removal_of_a_history_request_runs_on_its_record_without_looking_in_the_library()
        {
            Mocker.GetMock<IRequestService>().Setup(r => r.Find(7)).Returns(new Request { Id = 7, Title = "Heat", Status = RequestStatus.NotInLibrary });

            await Subject.StartRemovalAsync(7, null, false, Admin);

            Mocker.GetMock<IRemovalService>().Verify(r => r.StartRecordByAdminAsync(Admin, "owner (web app)", null, 7, It.Is<IReadOnlyCollection<int>>(s => s.Count == 0), false), Times.Once());
            Mocker.GetMock<IRemovalService>().Verify(r => r.TargetForRequestAsync(It.IsAny<int>()), Times.Never());
        }

        [Test]
        public async Task a_removal_of_a_title_not_in_the_library_says_why()
        {
            Mocker.GetMock<IRemovalService>().Setup(r => r.TargetForRequestAsync(7)).ReturnsAsync(LookupOutcome<RemovalTarget>.Fail("Not in the library."));

            var result = await Subject.StartRemovalAsync(7, null, false, Admin);

            result.Success.Should().BeFalse();
            result.Message.Should().Be("Not in the library.");
        }

        [Test]
        public async Task announcing_is_done_only_when_the_notice_was_posted()
        {
            var instance = new BackendInstance { Id = 3, Name = "Radarr", DownSince = System.DateTime.UtcNow };
            Mocker.GetMock<IBackendService>().Setup(b => b.Find(3)).Returns(instance);
            Mocker.GetMock<IConnectionMonitor>().Setup(m => m.AnnounceAsync(3, "owner (web app)")).Callback(() => instance.DownAnnounceMessageId = "99").ReturnsAsync("Announced");

            var result = await Subject.AnnounceOutageAsync(3, Admin);

            result.Success.Should().BeTrue();
            result.Message.Should().Be("Announced");
        }
    }

    [TestFixture]
    public class PortalMemberActionsFixture : CoreTest<PortalMemberActions>
    {
        private static readonly Actor Admin = Actor.Web("owner");

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IUserIdentityRepository>().Setup(i => i.GetForUser(It.IsAny<int>())).Returns(new List<UserIdentity>());
            Mocker.GetMock<IUserIdentityRepository>().Setup(i => i.GetForUser(1)).Returns(new List<UserIdentity> { new() { UserId = 1, Provider = UserIdentity.DiscordProvider, ExternalId = "111" } });
        }

        [Test]
        public void a_members_limit_is_set_on_their_discord_account()
        {
            Mocker.GetMock<IUserManagementService>().Setup(u => u.SetQuota(111, ContentType.Movie, 3, 7, Admin)).Returns(ServiceResult.Ok("Limit set"));

            Subject.SetLimit(1, ContentType.Movie, 3, 7, Admin).Success.Should().BeTrue();
        }

        [Test]
        public async Task a_member_without_a_discord_account_is_refused()
        {
            (await Subject.SuspendAsync(2, Admin)).Success.Should().BeFalse();
            Mocker.GetMock<IUserManagementService>().Verify(u => u.SuspendAsync(It.IsAny<ulong>(), It.IsAny<Actor>(), It.IsAny<string>()), Times.Never());
        }
    }
}
