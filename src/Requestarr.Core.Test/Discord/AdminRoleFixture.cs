using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Discord
{
    // Who is an admin: the server owner and whoever holds the admin role. Setup creates the role when it's missing
    // (or adopts one by its configured name or id) and gives it the admin channel and the request channels.
    [TestFixture]
    public class AdminRoleFixture : DiscordScenario
    {
        private async Task<DiscordGuildConfig> FreshSetup(DiscordSettings settings)
        {
            Mocker.Resolve<IDiscordSettingsStore>().Set(settings);
            Discord.Roles.Remove(AdminRole);
            Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();
            return await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
        }

        private static DiscordSettings Settings(ulong? roleId = null, string roleName = null) =>
            new() { BotToken = "t", ApplicationId = 1, GuildId = FakeDiscordGuild.Guild, AdminRoleId = roleId, AdminRoleName = roleName ?? DiscordSettings.DefaultAdminRoleName };

        private bool IsAdmin(FakeInteraction interaction) => Mocker.Resolve<ICallerResolver>().Resolve(interaction.Build()).IsAdmin;

        private FakeInteraction CardCommandBy(ulong member) =>
            FakeInteraction.SlashCommand("admin user").WithOption("member", Alice).By(member).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel);

        [Test]
        public void server_owner_should_be_admin()
        {
            var caller = Mocker.Resolve<ICallerResolver>().Resolve(AsAdmin(FakeInteraction.SlashCommand("admin user").WithOption("member", Alice)).Build());

            caller.IsAdmin.Should().BeTrue();
            caller.AdminDenial.Should().BeNull();
        }

        [Test]
        public void admin_role_member_should_be_admin_and_ordinary_member_should_not()
        {
            IsAdmin(CardCommandBy(Moderator).WithRoles(AdminRole)).Should().BeTrue();
            IsAdmin(CardCommandBy(Alice)).Should().BeFalse();
        }

        [Test]
        public async Task setup_should_create_requestarr_admin_role_when_missing()
        {
            var config = await FreshSetup(Settings());

            var role = ulong.Parse(config.AdminRoleId);
            Discord.Roles[role].Should().Be("Requestarr Admin");
            AuditTrail.Should().Contain(a => a.Action == "discord.admin-role.create");
        }

        [Test]
        public async Task setup_should_reuse_role_with_the_configured_name()
        {
            var existing = await Discord.CreateRoleAsync("Media Admins");

            var config = await FreshSetup(Settings(roleName: "Media Admins"));

            config.AdminRoleId.Should().Be(existing.ToString());
            Discord.Roles.Values.Count(r => r == "Media Admins").Should().Be(1);
        }

        [Test]
        public async Task setup_should_prefer_explicit_role_id()
        {
            var explicitRole = await Discord.CreateRoleAsync("Staff");

            var config = await FreshSetup(Settings(explicitRole));

            config.AdminRoleId.Should().Be(explicitRole.ToString());
            Discord.Roles.Values.Should().NotContain("Requestarr Admin");
        }

        [Test]
        public async Task setup_should_add_the_role_to_existing_admin_channel_and_categories()
        {
            var config = await FreshSetup(Settings());
            var role = ulong.Parse(config.AdminRoleId);

            OverwriteFor(Discord.Channels[AdminChannel], role).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
            OverwriteFor(Discord.Channels[ulong.Parse(config.RequestCategoryIds.Single())], role).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
        }

        [Test]
        public async Task holding_the_created_role_makes_a_member_admin()
        {
            var config = await FreshSetup(Settings());

            IsAdmin(CardCommandBy(Bob).WithRoles(ulong.Parse(config.AdminRoleId))).Should().BeTrue();
        }

        [Test]
        public async Task new_user_channels_should_allow_the_created_admin_role()
        {
            var config = await FreshSetup(Settings());
            await AddUser(Alice);

            OverwriteFor(ChannelFor(Alice), ulong.Parse(config.AdminRoleId)).Allow.Should().HaveFlag(ChannelPermissions.ViewChannel);
        }
    }
}
