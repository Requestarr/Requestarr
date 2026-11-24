using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Discord
{
    // Where commands and buttons work: admin actions only by admins in the admin channel; user actions only in the
    // caller's own channel, by an active member; nothing from another server. Checked on every command and button,
    // from the person acting and never from the custom ID.
    [TestFixture]
    public class CommandScopeFixture : RequestScenario
    {
        private const ulong OtherServer = 42;

        private async Task<string> ReplyTo(FakeInteraction interaction) => (await Harness.SendAsync(interaction)).LastReply.Content;

        private FakeInteraction RequestBy(ulong user) => FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune").By(user);

        private FakeInteraction PlexFormBy(ulong user) => FakeInteraction.ModalSubmit(PanelRoutes.LinkPlexForm).WithField(PanelRoutes.PlexUsernameField, "x").By(user);

        [Test]
        public async Task admin_actions_by_a_non_admin_are_refused_even_with_a_valid_custom_id()
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            var suspend = await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            suspend.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            suspend.LastReply.Ephemeral.Should().BeTrue();

            (await ReplyTo(FakeInteraction.SlashCommand("admin user").WithOption("member", Bob).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).Should().Be(DiscordText.AdminOnly);
            (await ReplyTo(FakeInteraction.Button("msreq-approve:1").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).Should().Be(DiscordText.AdminOnly);
            (await ReplyTo(FakeInteraction.Button($"adm-suspend:{Bob}").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.AdminOnly, "who acts is checked before where");
            UserFor(Bob).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task admin_actions_outside_the_admin_channel_are_refused_for_the_owner_too()
        {
            await Harness.SendAsync(PlexForm(Alice, "alice_plex"));

            (await ReplyTo(FakeInteraction.Button($"adm-suspend:{Bob}").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.AdminChannelOnly);
            (await ReplyTo(FakeInteraction.Button(PanelRoutes.AdminQueue).By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.AdminChannelOnly);
            (await ReplyTo(FakeInteraction.Button("msreq-approve:1").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.AdminChannelOnly);
            UserFor(Bob).Status.Should().Be(UserStatus.Active);
        }

        [Test]
        public async Task an_admin_role_member_can_act_in_the_admin_channel()
        {
            var run = await Harness.SendAsync(FakeInteraction.Button($"adm-suspend:{Bob}").By(Moderator, "mod").WithRoles(AdminRole).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.UserSuspendedDone("bob"));
        }

        [Test]
        public async Task user_actions_only_work_in_the_users_own_channel()
        {
            (await ReplyTo(RequestBy(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)))).Should().Be(DiscordText.OwnChannelOnly);
            (await ReplyTo(RequestBy(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).Should().Be(DiscordText.OwnChannelOnly);
            (await ReplyTo(PlexFormBy(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)))).Should().Be(DiscordText.OwnChannelOnly);
            (await ReplyTo(FakeInteraction.Button(PanelRoutes.MyRequests).By(Bob).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.OwnChannelOnly);
        }

        [Test]
        public async Task an_admin_using_a_user_action_in_the_admin_channel_is_pointed_to_the_admin_command()
        {
            (await ReplyTo(PlexFormBy(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel))).Should().Be(DiscordText.AdminUseUserCommand);
        }

        [Test]
        public async Task user_actions_by_someone_not_added_are_refused()
        {
            (await ReplyTo(PlexFormBy(Moderator).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.NotAUser);
        }

        [Test]
        public async Task a_suspended_user_cannot_request_use_the_panel_or_link_plex()
        {
            await Harness.SendAsync(AdminCommand("user suspend", Alice));

            (await ReplyTo(Search(Alice, "movie", "dune"))).Should().Be(DiscordText.Suspended);
            (await ReplyTo(PanelButton(Alice, PanelRoutes.MyRequests))).Should().Be(DiscordText.Suspended);
            (await ReplyTo(PlexForm(Alice, "x"))).Should().Be(DiscordText.Suspended);
        }

        [Test]
        public async Task commands_from_another_server_are_refused()
        {
            (await ReplyTo(RequestBy(Alice).InGuild(OtherServer).InChannel(ChannelOf(Alice)))).Should().Be(DiscordText.WrongServer);
            (await ReplyTo(AsAdmin(FakeInteraction.SlashCommand("admin user").WithOption("member", Alice)).InGuild(OtherServer))).Should().Be(DiscordText.WrongServer);
        }
    }
}
