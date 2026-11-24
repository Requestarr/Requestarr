using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Admin
{
    // The admin channel has one panel (always its newest message, never pinned): Pending queue with what waits,
    // Users, Members, Remove a title and Help, for admins in the admin channel only.
    [TestFixture]
    public class AdminPanelFixture : RequestScenario
    {
        [SetUp]
        public async Task Panels()
        {
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
        }

        private FakeDiscordGuild.FakeChannel AdminRoom => Discord.Channels[AdminChannel];

        [Test]
        public async Task admin_channel_has_one_panel_with_queue_and_users_even_after_a_restart()
        {
            await Mocker.Resolve<IPanelService>().EnsureAdminPanelAsync();

            AdminRoom.Pinned.Should().BeEmpty();
            PanelsIn(AdminRoom, PanelRoutes.AdminQueue).Should().Be(1);
            PanelIn(AdminRoom).Buttons.Select(b => (b.Label, b.CustomId)).Should().Equal(
                (DiscordText.PanelPendingQueue, PanelRoutes.AdminQueue),
                (DiscordText.PanelUsers, PanelRoutes.AdminUsers),
                (DiscordText.PanelMembers, "adm-pick"),
                (DiscordText.RemoveTitleLabel, "rm-search"),
                (DiscordText.PanelHelp, PanelRoutes.AdminHelp));
            Config.AdminPanelMessageId.Should().Be(AdminRoom.MessageIds.Last().ToString());
        }

        [Test]
        public async Task pending_queue_button_starts_the_walk_and_the_panel_counts_what_waits()
        {
            (await AdminButton(PanelRoutes.AdminQueue)).LastReply.Content.Should().Be(DiscordText.WalkDone);

            await RequestMovie(Alice, Dune);
            var run = await AdminButton(PanelRoutes.AdminQueue);

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().StartWith(DiscordText.WalkHeader(1, 1)).And.Contain("Dune (2021)").And.Contain("**alice** (asked)");

            await Mocker.Resolve<IPanelService>().RefreshAdminPanelAsync();
            PanelIn(AdminRoom).Buttons.First().Label.Should().Be(DiscordText.PanelPendingQueueCount(1)).And.Be("Pending queue (1)");
        }

        [Test]
        public async Task users_button_lists_users_with_access()
        {
            var text = (await AdminButton(PanelRoutes.AdminUsers)).LastReply.Content;

            text.Should().Contain("• alice · active").And.Contain("• bob · active").And.Contain("Movies: needs approval");
        }

        [Test]
        public async Task admin_panel_buttons_are_admin_only_and_admin_channel_only()
        {
            (await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.AdminQueue).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel)))
                .LastReply.Content.Should().Be(DiscordText.AdminOnly);
            (await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.AdminUsers).By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice))))
                .LastReply.Content.Should().Be(DiscordText.AdminChannelOnly);
        }
    }
}
