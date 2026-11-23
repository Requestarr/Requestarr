using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Welcome
{
    // Media server access from Discord. Each connected media server gets a button on the welcome message, and Link Plex
    // on member panels (with its help topic) shows only while a Plex server is connected. Asking for a media server
    // alone goes to the admin like Link Plex; asked for together with Requestarr, it follows the approval.
    [TestFixture]
    public class MediaServerAccessFixture : WelcomeScenario
    {
        private Requestarr.Core.Discord.Interactions.InteractionReply WelcomeMessage => PanelIn(Discord.Channels[WelcomeChannel]);

        private async Task<string[]> HelpTopics(ulong member)
        {
            var run = await Harness.SendAsync(FakeInteraction.Button(PanelRoutes.Help).By(member).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(member)));
            return run.LastReply.SelectMenu.Options.Select(o => o.Value).ToArray();
        }

        [Test]
        public async Task a_connected_media_server_gets_its_own_button()
        {
            ConnectedMediaServers.Servers.Add(MediaServerProvider.Plex);
            await Mocker.Resolve<IPanelService>().EnsureWelcomeAsync();

            WelcomeMessage.Cards.Single().Buttons.Select(b => b.CustomId).Should().Equal(WelcomeRoutes.Request, $"{WelcomeRoutes.Media}:plex");
            WelcomeMessage.Cards.Single().Text.Should().Contain("Plex");
        }

        [Test]
        public async Task without_a_connected_media_server_its_button_does_nothing()
        {
            var run = await Press(Carol, $"{WelcomeRoutes.Media}:plex");

            run.LastReply.Content.Should().Be(DiscordText.MediaServerNotConnected(MediaServerProvider.Plex));
            run.Responder.LastModal.Should().BeNull();
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task link_plex_and_its_help_topic_show_only_while_a_plex_server_is_connected(bool connected)
        {
            if (connected)
            {
                ConnectedMediaServers.Servers.Add(MediaServerProvider.Plex);
            }

            await AddUser(Alice);

            PanelIn(ChannelFor(Alice)).Buttons.Select(b => b.CustomId).Contains(PanelRoutes.LinkPlex).Should().Be(connected);
            (await HelpTopics(Alice)).Contains("plex").Should().Be(connected);
        }

        [Test]
        public void the_admin_help_explains_why_media_server_buttons_are_hidden()
        {
            var mediaserver = HelpCatalog.AdminTopics.Single(t => t.Key == "mediaserver").Intro(HelpViewer.Everything);
            var access = HelpCatalog.AdminTopics.Single(t => t.Key == "access").Intro(HelpViewer.Everything);

            mediaserver.Should().Contain("only show for a media server you've connected").And.Contain("hidden for now");
            access.Should().Contain("no media server buttons").And.Contain("Link Plex is gone from member panels");
        }

        [Test]
        public async Task media_server_access_alone_goes_to_the_admin_like_link_plex()
        {
            ConnectedMediaServers.Servers.Add(MediaServerProvider.Plex);
            await Mocker.Resolve<IPanelService>().EnsureWelcomeAsync();

            var open = await Press(Carol, $"{WelcomeRoutes.Media}:plex");
            open.Responder.LastModal.CustomId.Should().Be($"{WelcomeRoutes.MediaForm}:plex");

            var sent = await Harness.SendAsync(FakeInteraction.ModalSubmit($"{WelcomeRoutes.MediaForm}:plex").WithField(WelcomeRoutes.UsernameField, "carol_plex")
                .By(Carol, "carol").InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));

            sent.LastReply.Content.Should().Be(DiscordText.AccessRequestSent(MediaServerProvider.Plex));
            AdminRoom.Messages.Last().Buttons.Select(b => b.CustomId).Should().Equal("msreq-approve:1", "msreq-deny:1");
            UserFor(Carol).PrivateChannelId.Should().BeNull("asking for Plex doesn't add anyone to Requestarr");
            (await Press(Carol, $"{WelcomeRoutes.Media}:plex")).LastReply.Content.Should().Be(DiscordText.AccessRequestAlreadyPending);
        }

        [Test]
        public async Task media_server_access_together_with_requestarr_follows_the_approval()
        {
            ConnectedMediaServers.Servers.Add(MediaServerProvider.Plex);
            var click = await Press(Carol);
            click.LastReply.SelectMenu.Options.Select(o => o.Value).Should().Equal("movie", "tv", "media:plex");

            var picked = await Harness.SendAsync(FakeInteraction.SelectMenu(WelcomeRoutes.Types, "movie", "media:plex").By(Carol).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));
            var form = await Harness.SendAsync(FakeInteraction.Button(picked.LastReply.Buttons.Single().CustomId).By(Carol).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));
            form.Responder.LastModal.Fields.Select(f => f.CustomId).Should().Equal(WelcomeRoutes.NoteField, WelcomeRoutes.UsernameField);

            await Harness.SendAsync(FakeInteraction.ModalSubmit(form.Responder.LastModal.CustomId).WithField(WelcomeRoutes.UsernameField, "carol_plex").By(Carol, "carol").InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));
            LastRequest.MediaProvider.Should().Be(MediaServerProvider.Plex);

            await Approve(LastRequest.Id);

            Mocker.Resolve<IMediaServerAccessRequestRepository>().GetPendingForUser(UserFor(Carol).Id).Single().Username.Should().Be("carol_plex", "it goes to the admin the same way as Link Plex");
        }
    }
}
