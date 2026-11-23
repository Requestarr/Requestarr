using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Welcome
{
    // The bot recognises its own panels, groups and channels by what they are, not only by the IDs in its database, so
    // a restart with a new database or a reset never makes a second one; a member who returns gets their one channel
    // back, and loose channels go back into their group.
    [TestFixture]
    public class NoDuplicatePanelsOrChannelsFixture : WelcomeScenario
    {
        private int Panels(ulong channel, string route) => PanelsIn(Discord.Channels[channel], route);

        private int ChannelsOf(string slug) => Discord.Channels.Values.Count(c => !c.IsCategory && c.Name.EndsWith("requests-" + slug));

        // A new database on the same server: everything Requestarr stored is gone, the server is as it was.
        private async Task StartWithANewDatabase()
        {
            Mocker.Resolve<IDiscordGuildConfigRepository>().Purge();
            Mocker.Resolve<IRequestarrUserRepository>().Purge();
            Mocker.Resolve<IUserIdentityRepository>().Purge();
            Mocker.Resolve<IUserMediaAccessRepository>().Purge();
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            await Mocker.Resolve<IPanelService>().EnsureAllAsync();
        }

        [Test]
        public async Task a_new_database_finds_the_panels_groups_and_channels_already_there()
        {
            var channels = Discord.Channels.Count;

            await StartWithANewDatabase();
            await StartWithANewDatabase();

            Discord.Channels.Count.Should().Be(channels, "no second group or channel");
            Panels(AdminChannel, PanelRoutes.AdminQueue).Should().Be(1);
            Panels(WelcomeChannel, WelcomeRoutes.Request).Should().Be(1);
            Discord.Channels[BlockedChannel].Messages.Count(m => m.Cards.Any(c => c.Text == DiscordText.BlockedMessage)).Should().Be(1);
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId, "and it's the newest message");
        }

        [Test]
        public async Task the_blocked_channel_set_up_again_has_only_the_paused_message_and_its_new_topic()
        {
            // How it read before November 14, 2026: another title and topic.
            var blocked = Discord.Channels[BlockedChannel];
            blocked.Topic = "Why you can't request access through Requestarr any more.";
            await Discord.SendMessageAsync(BlockedChannel, new InteractionReply { Cards = { new InteractionCard { Text = "## You can't request access through Requestarr any more\nThe buttons..." } } });

            await StartWithANewDatabase();

            blocked.Topic.Should().Be(DiscordText.BlockedChannelTopic);
            blocked.Messages.Select(m => m.Cards.Single().Text).Should().Equal(DiscordText.BlockedMessage);
        }

        [Test]
        public async Task copies_of_a_panel_are_removed_leaving_exactly_one()
        {
            await Discord.SendMessageAsync(AdminChannel, PanelService.AdminPanel());
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "a version card" });
            await Discord.SendMessageAsync(AdminChannel, PanelService.AdminPanel());

            await Mocker.Resolve<IPanelService>().KeepLastAsync(AdminChannel);

            Panels(AdminChannel, PanelRoutes.AdminQueue).Should().Be(1);
            AdminRoom.MessageIds.Last().ToString().Should().Be(Config.AdminPanelMessageId);
            AdminRoom.Messages[^2].Content.Should().Be("a version card", "the card sits right above the one panel");
        }

        [Test]
        public async Task a_member_added_again_with_a_new_database_gets_their_one_channel_back()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            var role = RoleOf(Alice);

            await StartWithANewDatabase();
            await AddUser(Alice);

            ChannelOf(Alice).Should().Be(channel);
            RoleOf(Alice).Should().Be(role);
            ChannelsOf("alice").Should().Be(1);
            PanelsIn(ChannelFor(Alice), PanelRoutes.MyRequests).Should().Be(1);
        }

        [Test]
        public async Task a_removed_member_who_returns_gets_their_archived_channel_back_live()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            await Harness.SendAsync(AdminCommand("user remove", Alice));
            Discord.Channels[channel].Name.Should().StartWith(ChannelLayout.ArchivedPrefix);

            await AskForAccess(Alice, null, "movie");
            var approved = await Approve(LastRequest.Id);

            ChannelOf(Alice).Should().Be(channel, "their one channel, messages kept");
            Discord.Channels[channel].Name.Should().Be("requests-alice");
            Discord.Channels[channel].CategoryId.Should().Be(ulong.Parse(Config.RequestCategoryIds[0]));
            OverwriteFor(Discord.Channels[channel], RoleOf(Alice)).Allow.Should().HaveFlag(ChannelPermissions.SendMessages);
            approved.LastReply.Content.Should().Contain($"<#{channel}>");
            ChannelsOf("alice").Should().Be(1);
        }

        [Test]
        public async Task the_archived_channel_is_found_by_name_too()
        {
            await AddUser(Alice);
            var channel = ChannelOf(Alice);
            await Harness.SendAsync(AdminCommand("user remove", Alice));
            Mocker.Resolve<IRequestarrUserRepository>().Purge();
            Mocker.Resolve<IUserIdentityRepository>().Purge();

            await AddUser(Alice);

            ChannelOf(Alice).Should().Be(channel);
            Discord.Channels[channel].Name.Should().Be("requests-alice");
        }

        [Test]
        public async Task channels_left_loose_when_a_group_was_deleted_go_back_in()
        {
            await AddUser(Alice);
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            var loose = await Discord.CreateTextChannelAsync("requests-carol", null, null, System.Array.Empty<PermissionOverwrite>());
            await Discord.DeleteChannelAsync(ulong.Parse(Config.RequestCategoryIds[0]));
            await Discord.DeleteChannelAsync(ulong.Parse(Config.AdminCategoryId));

            foreach (var channel in Discord.Channels.Values.Where(c => !c.IsCategory && c.CategoryId.HasValue && !Discord.Channels.ContainsKey(c.CategoryId.Value)))
            {
                channel.CategoryId = null;
            }

            await StartWithANewDatabase();

            var users = ulong.Parse(Config.RequestCategoryIds[0]);
            Discord.Channels.Values.Where(c => !c.IsCategory).Should().OnlyContain(c => c.CategoryId != null, "nothing left at the top level");
            Discord.ChannelNamed("requests-alice").CategoryId.Should().Be(users, "known by name even with a new database");
            Discord.Channels[loose].CategoryId.Should().Be(users);
            Discord.Channels[AdminChannel].CategoryId.Should().Be(ulong.Parse(Config.AdminCategoryId));
        }
    }
}
