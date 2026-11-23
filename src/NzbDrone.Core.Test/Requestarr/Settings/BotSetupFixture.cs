using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Backends.MediaManagers;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Settings
{
    // The first-run setup with only the bot's token: what the bot learns from Discord is saved, the admins are picked
    // in the web app (the server's owner always one), and a new Sonarr or Radarr starts from defaults that work.
    [TestFixture]
    public class BotSetupFixture : CoreTest<DiscordAdmins>
    {
        private const ulong Role = 900;
        private const ulong Owner = 1;
        private static readonly Actor Admin = Actor.Web("owner");

        private static GuildMember Member(ulong id, string name, params ulong[] roles) => new(id, name.ToLowerInvariant(), name, null, false, roles);

        private void GivenServer(params GuildMember[] members)
        {
            var gateway = Mocker.GetMock<IDiscordGuildGateway>();
            gateway.SetupGet(g => g.IsConnected).Returns(true);
            gateway.SetupGet(g => g.OwnerId).Returns(Owner);
            gateway.Setup(g => g.GetMembersAsync()).ReturnsAsync(members.ToList());
            gateway.Setup(g => g.GetMemberAsync(It.IsAny<ulong>())).ReturnsAsync((ulong id) => members.FirstOrDefault(m => m.Id == id));
            Mocker.GetMock<IGuildSetupService>().SetupGet(g => g.AdminRoleId).Returns(Role);
        }

        [Test]
        public async Task the_admins_are_who_holds_the_role_and_the_owner_always_is()
        {
            GivenServer(Member(Owner, "Owner"), Member(2, "Alex", Role), Member(3, "Sam"), new GuildMember(4, "bot", "Bot", null, true, new List<ulong>()));

            var list = await Subject.ListAsync();

            list.Select(c => (c.Name, c.IsAdmin, c.IsOwner)).Should().Equal(("Owner", true, true), ("Alex", true, false), ("Sam", false, false));
            list.Single(c => c.Name == "Alex").DiscordId.Should().Be("2", "a Discord ID is text for the browser");
        }

        [Test]
        public async Task saving_admins_gives_and_takes_the_role_but_never_from_the_owner()
        {
            GivenServer(Member(Owner, "Owner"), Member(2, "Alex", Role), Member(3, "Sam"));

            var result = await Subject.SetAsync(new ulong[] { 3 }, Admin);

            result.Saved.Should().BeTrue();
            var gateway = Mocker.GetMock<IDiscordGuildGateway>();
            gateway.Verify(g => g.AddRoleToMemberAsync(3, Role), Times.Once());
            gateway.Verify(g => g.AddRoleToMemberAsync(Owner, Role), Times.Once());
            gateway.Verify(g => g.RemoveRoleFromMemberAsync(2, Role), Times.Once());
            gateway.Verify(g => g.RemoveRoleFromMemberAsync(Owner, It.IsAny<ulong>()), Times.Never());
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, DiscordAdmins.ChangeAction, "settings", "admins", "Admins: added Owner, Sam; removed Alex"), Times.Once());
        }

        [Test]
        public async Task the_owner_gets_the_admin_role_when_the_server_is_set_up_but_not_where_a_file_names_the_admins()
        {
            GivenServer(Member(Owner, "Owner"));

            await Subject.EnsureOwnerAsync();
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.AddRoleToMemberAsync(Owner, Role), Times.Once());

            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);
            await Subject.EnsureOwnerAsync();
            (await Subject.SetAsync(new ulong[] { 2 }, Admin)).Saved.Should().BeFalse();
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.AddRoleToMemberAsync(Owner, Role), Times.Once());
        }

        [Test]
        public void what_the_bot_learns_is_saved_and_its_server_choice_recorded_but_never_over_a_provisioning_file()
        {
            var config = Mocker.GetMock<IConfigService>();
            config.SetupProperty(c => c.DiscordApplicationId, string.Empty);
            config.SetupProperty(c => c.DiscordGuildId, string.Empty);
            var choice = Mocker.Resolve<DiscordServerChoice>();

            choice.SaveApplication(42);
            choice.SaveServer(7, Admin).Should().BeTrue();

            (config.Object.DiscordApplicationId, config.Object.DiscordGuildId).Should().Be(("42", "7"));
            Mocker.GetMock<IStoredSettings>().Verify(s => s.Load(), Times.Exactly(2));
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, AppSettingsService.ChangeAction, "settings", DiscordServerChoice.ServerKey, "Server: from nothing to 7"), Times.Once());

            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);
            choice.SaveServer(8, Admin).Should().BeFalse();
            config.Object.DiscordGuildId.Should().Be("7");
        }

        [Test]
        public void a_new_sonarr_starts_with_no_address_standard_series_and_new_members_approved_automatically()
        {
            var sonarr = new Sonarr(Mock.Of<IInstanceSettingsService>(), Mock.Of<IBackendRepository>());
            var settings = ((BackendInstance)sonarr.DefaultDefinitions.Single()).Config;

            settings.Url.Should().BeNull("inside Requestarr's container, localhost is Requestarr itself");
            settings.SeriesType.Should().Be(BackendSettings.StandardSeries);
            settings.DefaultAccess.Should().Be(AccessState.AutoApprove);
            new BackendSettings { SeriesType = null }.SeriesType.Should().Be(BackendSettings.StandardSeries, "a saved instance without one reads as Standard");
            new BackendSettings().DefaultAccess.Should().Be(AccessState.NeedsApproval, "an instance saved before the setting keeps what it had");
        }

        [Test]
        public void the_access_choices_read_most_open_first_everywhere()
        {
            AccessStates.Shown.Should().Equal(AccessState.AutoApprove, AccessState.NeedsApproval, AccessState.Off);
            ((int)AccessState.Off, (int)AccessState.NeedsApproval, (int)AccessState.AutoApprove).Should().Be((0, 1, 2), "the stored numbers never change");
        }
    }
}
