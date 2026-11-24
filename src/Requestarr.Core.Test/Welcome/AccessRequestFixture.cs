using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Welcome
{
    // The welcome message says what Requestarr is and has Request access. A request goes to the admin as a card and an
    // /admin requests item; the admin approves with real access levels (the person is added exactly as from the panel)
    // or denies with a reason, and the person hears privately. One open request; after a denial, a cooldown.
    [TestFixture]
    public class AccessRequestFixture : WelcomeScenario
    {
        private InteractionReply WelcomeMessage => PanelIn(Discord.Channels[WelcomeChannel]);

        [Test]
        public void the_welcome_message_says_what_requestarr_is_and_has_request_access_only()
        {
            WelcomeMessage.Cards.Single().Text.Should().StartWith("# Welcome to Requestarr\n").And.Contain("movies and TV shows").And.Contain("private channel").And.Contain(ChannelLayout.UsersCategoryName);
            WelcomeMessage.Cards.Single().Color.Should().Be(DiscordText.WelcomeColor);
            WelcomeMessage.Cards.Single().Buttons.Select(b => (b.Label, b.CustomId)).Should().Equal((DiscordText.RequestAccessLabel, WelcomeRoutes.Request));
            PanelIn(Discord.Channels[BlockedChannel]).Cards.Single().Text.Should().Be(DiscordText.BlockedMessage).And.StartWith("## ");
            PanelIn(Discord.Channels[BlockedChannel]).Cards.Single().Color.Should().Be(DiscordText.BlockedColor);
            Discord.Channels[WelcomeChannel].Messages.Should().ContainSingle("the one message the bot keeps current");
        }

        [Test]
        public async Task the_form_offers_only_connected_types_then_sends_a_card_and_a_queue_item()
        {
            var click = await Press(Carol);
            click.LastReply.Ephemeral.Should().BeTrue();
            click.LastReply.SelectMenu.Options.Select(o => o.Label).Should().Equal("Movies", "TV");
            click.LastReply.Buttons.Single().Disabled.Should().BeTrue("Continue waits for a choice");

            var sent = await AskForAccess(Carol, "Friend of Alice", "movie", "tv");

            sent.LastReply.Content.Should().Be(DiscordText.AccessRequestSentToAdmin);
            var request = LastRequest;
            (request.DiscordUserId, request.ContentTypes, request.Note, request.Status).Should().Be((Carol.ToString(), request.ContentTypes, "Friend of Alice", AccessRequestState.Pending));
            request.ContentTypes.Should().Equal(ContentType.Movie, ContentType.Tv);

            var card = AdminRoom.Messages.Single(m => m.Content != null && m.Content.StartsWith("🙋"));
            card.Content.Should().Contain("**carol**").And.Contain($"<@{Carol}>").And.Contain("Would like: Movies, TV").And.Contain("Friend of Alice");
            card.Buttons.Select(b => b.CustomId).Should().Equal($"{AccessRoutes.Approve}:{request.Id}", $"{AccessRoutes.Deny}:{request.Id}");
            Mocker.Resolve<IAdminQueue>().Items().Select(i => i.Key).Should().Contain($"a{request.Id}");
            Discord.Channels[WelcomeChannel].Messages.Should().ContainSingle("nothing is posted in the welcome channel");
        }

        [Test]
        public async Task the_admin_approves_with_real_access_levels_and_the_person_is_added_and_told_privately()
        {
            await AskForAccess(Carol, null, "movie");
            var request = LastRequest;

            var run = await Approve(request.Id, ("movie", "auto"), ("tv", "approval"));

            var user = UserFor(Carol);
            user.Should().NotBeNull();
            user.PrivateChannelId.Should().NotBeNull("the channel is created exactly as adding from the panel does");
            var access = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(user.Id).ToDictionary(a => a.ContentType, a => a.State);
            (access[ContentType.Movie], access[ContentType.Tv], access[ContentType.Music]).Should().Be((AccessState.AutoApprove, AccessState.NeedsApproval, AccessState.Off));
            run.LastReply.Content.Should().Contain("Approved by owner").And.Contain($"<#{user.PrivateChannelId}>");
            LastDirectMessage(Carol).Content.Should().Contain("You're in!").And.Contain($"<#{user.PrivateChannelId}>");
            LastRequest.Status.Should().Be(AccessRequestState.Approved);
            AdminRoom.Messages.Single(m => m.Content != null && m.Content.StartsWith("🙋")).Buttons.Should().BeEmpty("the card shows the outcome instead");
            Mocker.Resolve<IAdminQueue>().Items().Should().NotContain(i => i.Key == $"a{request.Id}");
        }

        // Admins ask the same way and get their own request channel straight away: what they picked on automatic
        // approval, nothing else, and no card in the admin channel waiting on themselves.
        [Test]
        public async Task an_admin_asking_gets_their_own_channel_straight_away()
        {
            var run = await AskForAccess(FakeDiscordGuild.Owner, null, "movie");

            var user = UserFor(FakeDiscordGuild.Owner);
            user.PrivateChannelId.Should().NotBeNull("admins make their requests in their own channel, like everyone");
            run.LastReply.Content.Should().Be(DiscordText.AdminChannelReady(user.PrivateChannelId));
            var access = Mocker.Resolve<IUserMediaAccessRepository>().GetForUser(user.Id).ToDictionary(a => a.ContentType, a => a.State);
            (access[ContentType.Movie], access[ContentType.Tv]).Should().Be((AccessState.AutoApprove, AccessState.Off));
            LastRequest.Status.Should().Be(AccessRequestState.Approved);
            AdminRoom.Messages.Should().NotContain(m => m.Content != null && m.Content.StartsWith("🙋"), "nothing for an admin to decide");
            Mocker.Resolve<IAdminQueue>().Items().Should().BeEmpty();
            LastDirectMessage(FakeDiscordGuild.Owner).Should().BeNull("they were told right there");
        }

        [Test]
        public async Task an_admin_requesting_in_the_admin_channel_is_pointed_to_their_own_channel()
        {
            var before = await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune")));
            before.LastReply.Content.Should().Be(DiscordText.AdminRequestsInOwnChannel(null));
            before.LastReply.Ephemeral.Should().BeTrue();

            await AskForAccess(FakeDiscordGuild.Owner, null, "movie");
            var after = await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune")));

            after.LastReply.Content.Should().Be(DiscordText.AdminRequestsInOwnChannel(UserFor(FakeDiscordGuild.Owner).PrivateChannelId));
        }

        // The chooser starts each type they asked for at its instance's default access, and every other type off.
        [TestCase("tv", AccessState.NeedsApproval, "off,approval,off", "010")]
        [TestCase("movie,tv", AccessState.AutoApprove, "approval,auto,off", "120")]
        public async Task the_chooser_starts_from_what_they_asked_for_at_its_instances_default_access(string types, AccessState sonarrDefault, string defaults, string levels)
        {
            var sonarr = Mocker.Resolve<IBackendRepository>().FindByName("Sonarr");
            sonarr.Config.DefaultAccess = sonarrDefault;
            Mocker.Resolve<IBackendRepository>().Update(sonarr);
            await AskForAccess(Carol, null, types.Split(','));

            var chooser = (await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{AccessRoutes.Approve}:{LastRequest.Id}")))).LastReply;

            chooser.Ephemeral.Should().BeTrue();
            new[] { chooser.SelectMenu }.Concat(chooser.MoreMenus).Select(m => m.Options.Single(o => o.Default).Value).Should().Equal(defaults.Split(','));
            chooser.Buttons.Single().CustomId.Should().Be($"{AccessRoutes.Confirm}:{LastRequest.Id}:{levels}");
        }

        [Test]
        public async Task a_second_click_while_the_request_is_open_answers_with_the_status_and_creates_nothing()
        {
            await AskForAccess(Carol, null, "movie");

            var again = await Press(Carol);

            again.LastReply.Ephemeral.Should().BeTrue();
            again.LastReply.Content.Should().Be(DiscordText.WelcomePending(Now));
            again.LastReply.SelectMenu.Should().BeNull("no second form");
            Mocker.Resolve<IAccessRequestRepository>().All().Should().ContainSingle();
            AdminRoom.Messages.Count(m => m.Content != null && m.Content.StartsWith("🙋")).Should().Be(1, "no message to the admin per click");
        }

        [Test]
        public async Task a_denial_with_a_reason_is_told_privately_and_the_cooldown_refuses_until_it_passes()
        {
            await AskForAccess(Carol, null, "movie");
            var run = await Deny(LastRequest.Id, "Family only, sorry");

            run.LastReply.Content.Should().Contain("Declined by owner: Family only, sorry");
            var askAgain = Now.AddDays(AccessSettings.DefaultDenialCooldownDays);
            LastDirectMessage(Carol).Content.Should().Contain("declined: Family only, sorry").And.Contain($"<t:{new DateTimeOffset(askAgain).ToUnixTimeSeconds()}:R>");
            UserFor(Carol).Should().BeNull("nobody is added by asking");

            Now = Now.AddDays(3);
            var early = await Press(Carol);
            early.LastReply.Content.Should().Be(DiscordText.WelcomeDeniedWait("Family only, sorry", askAgain));
            early.LastReply.Content.Should().StartWith("Your last request was declined: Family only, sorry. You can ask again");
            Mocker.Resolve<IAccessRequestRepository>().All().Should().ContainSingle();

            Now = Now.AddDays(5);
            (await Press(Carol)).LastReply.SelectMenu.CustomId.Should().Be(WelcomeRoutes.Types, "after the cooldown they can ask again");
        }

        [Test]
        public async Task members_already_added_are_told_where_their_channel_is()
        {
            await AddUser(Alice);

            var run = await Press(Alice);

            run.LastReply.Content.Should().Be(DiscordText.WelcomeAlreadyIn(UserFor(Alice).PrivateChannelId));
            Mocker.Resolve<IAccessRequestRepository>().All().Should().BeEmpty();
        }

        [Test]
        public async Task the_walk_shows_access_requests_with_approve_and_deny()
        {
            await AskForAccess(Carol, "please", "movie");

            var walk = await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin requests")));

            walk.LastReply.Content.Should().Contain("🙋 **Access request** from **carol**").And.Contain("please");
            walk.LastReply.Buttons.Select(b => b.CustomId).Should().Contain($"{AccessRoutes.Approve}:{LastRequest.Id}").And.Contain($"{AccessRoutes.Deny}:{LastRequest.Id}");
            await Mocker.Resolve<IPanelService>().RefreshAdminPanelAsync();
            PanelIn(AdminRoom).Buttons.First().Label.Should().Be(DiscordText.PanelPendingQueueCount(1), "the admin panel counts it");
        }

        [Test]
        public async Task an_admin_added_from_the_panel_meanwhile_is_approved_without_adding_twice()
        {
            await AskForAccess(Carol, null, "movie");
            await AddUser(Carol);
            var channel = UserFor(Carol).PrivateChannelId;

            var run = await Approve(LastRequest.Id);

            run.LastReply.Content.Should().Contain("Approved");
            UserFor(Carol).PrivateChannelId.Should().Be(channel);
            LastRequest.Status.Should().Be(AccessRequestState.Approved);
        }

        [Test]
        public async Task only_admins_decide_and_only_in_the_admin_channel()
        {
            await AskForAccess(Carol, null, "movie");

            var run = await Harness.SendAsync(FakeInteraction.Button($"{AccessRoutes.Approve}:{LastRequest.Id}").By(Carol).InGuild(FakeDiscordGuild.Guild).InChannel(WelcomeChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            LastRequest.Status.Should().Be(AccessRequestState.Pending);
        }
    }
}
