using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Announcements
{
    // What's new as cards. In the admin and announcements channels each version is one line (since 0.6.0 its version
    // and date, then its main change and how many more) with Read changes; pressing it shows the full card (version and
    // date, For users and For admins, one line per change) only to whoever pressed it.
    [TestFixture]
    public class VersionCardsFixture : WelcomeScenario
    {
        private const string Text = @"# Changelog

## 0.2.0 (2026-10-22)

### For users
- **Follow:** follow a request from its card.
- **Limits:** your panel shows your limits.

### For admins
- **Member cards:** a clearer layout.
";

        // The Read changes button on the admin channel's card for the version above.
        private const string AdminsReadChanges = "whatsnew:a:0.2.0";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private ulong UsersChannel => ulong.Parse(Config.AnnouncementsChannelId);

        [SetUp]
        public async Task Posted()
        {
            Service.Changelog = () => ChangelogSource.FromMarkdown(Text);
            Service.CurrentVersion = () => new Version(0, 2, 0, 7);
            await Service.PostVersionNotesAsync();
        }

        // The card's year shows only when the version is from another year than the one the server runs in.
        private static string Dated(string title) => title + (DateTime.UtcNow.Year == 2026 ? string.Empty : ", 2026");

        // The admins' own change leads their card, which counts every other line too; members see only theirs.
        [TestCase(VersionAudience.Admins, "Member cards, and 2 more", "whatsnew:a:0.2.0")]
        [TestCase(VersionAudience.Users, "Follow, and 1 more", "whatsnew:u:0.2.0")]
        public void each_channels_card_is_the_version_and_its_date_then_its_main_change_and_how_many_more(VersionAudience audience, string description, string readChanges)
        {
            var admins = audience == VersionAudience.Admins;
            var card = Discord.Channels[admins ? AdminChannel : UsersChannel].Messages.Single(m => m.Embeds.Any());

            card.Content.Should().BeNullOrEmpty();
            card.Embeds.Single().Title.Should().Be(Dated("Requestarr 0.2.0 · October 22"));
            card.Embeds.Single().Description.Should().Be(description);
            card.Embeds.Single().Color.Should().Be(admins ? VersionCards.AdminsColor : VersionCards.UsersColor);
            card.Buttons.Single().Label.Should().Be(DiscordText.ReadChangesLabel);
            card.Buttons.Single().CustomId.Should().Be(readChanges);
        }

        [TestCase(2026, "2026-10-25", "Requestarr 0.4.11 · October 25")]
        [TestCase(2027, "2026-10-25", "Requestarr 0.4.11 · October 25, 2026")]
        [TestCase(2026, null, "Requestarr 0.4.11")]
        public void the_title_is_the_version_and_its_day_with_the_year_only_when_it_is_not_this_year(int thisYear, string day, string title)
        {
            var date = day == null ? (DateTime?)null : DateTime.Parse(day, CultureInfo.InvariantCulture);
            var version = new ChangelogVersion(new Version(0, 4, 11), ChangelogLines.Of("**Softer logo:** easier on the eyes."), ChangelogLines.Of(), date);

            VersionCards.Collapsed(version, VersionAudience.Users, thisYear).Title.Should().Be(title);
        }

        [Test]
        public void a_card_with_one_change_names_it_alone_and_a_build_keeps_its_label()
        {
            var version = new ChangelogVersion(new Version(0, 7, 4), ChangelogLines.Of("**Softer logo:** easier on the eyes."), ChangelogLines.Of(), new DateTime(2026, 11, 14));

            var card = VersionCards.Collapsed(version, VersionAudience.Users, 2026, "0.7.0004-qa.4f2a9c1");

            card.Title.Should().Be("Requestarr 0.7.0004-qa.4f2a9c1 · November 14");
            VersionCards.Collapsed(version, VersionAudience.Users, 2026).Title.Should().Be("Requestarr 0.7.0004 · November 14", "a build number shows four digits");
            card.Description.Should().Be("Softer logo");
            VersionCards.Collapsed(version, VersionAudience.Admins, 2026).Description.Should().Be("Softer logo", "the admin card covers the users' lines too");
        }

        [Test]
        public void a_card_is_found_by_its_read_changes_button_whatever_its_title()
        {
            VersionCards.Parse(new[] { "whatsnew:a:0.4.17" }, VersionAudience.Admins).Should().Be(new Version(0, 4, 17));
            VersionCards.Parse(new[] { "whatsnew:u:0.4.17" }, VersionAudience.Users).Should().Be(new Version(0, 4, 17));
            VersionCards.Parse(new[] { "whatsnew:u:0.4.17" }, VersionAudience.Admins).Should().BeNull("the other channel's card");
            VersionCards.Parse(new[] { "whatsnew:a:soon" }, VersionAudience.Admins).Should().BeNull();
            VersionCards.Parse(new[] { "admin:panel" }, VersionAudience.Admins).Should().BeNull();
            VersionCards.Parse(null, VersionAudience.Admins).Should().BeNull("a message without buttons");
        }

        [Test]
        public async Task read_changes_shows_the_full_card_privately()
        {
            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(AdminsReadChanges)));

            var full = run.LastReply;
            full.Ephemeral.Should().BeTrue("only the person who pressed it sees it");
            full.Embeds.Should().BeEmpty("cards show text at a message's size");
            full.Cards.Select(c => c.Text).Should().Equal(
                "## Requestarr 0.2.0 · 22 October 2026\n### For users\n**Changed**\n- **Follow:** follow a request from its card.\n- **Limits:** your panel shows your limits.",
                "### For admins\n**Changed**\n- **Member cards:** a clearer layout.");
            full.Cards.Select(c => c.Color).Should().Equal(VersionCards.UsersColor, VersionCards.AdminsColor);
        }

        [Test]
        public async Task pressing_read_changes_again_replaces_the_earlier_copy()
        {
            var first = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(AdminsReadChanges)));
            var second = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(AdminsReadChanges)));

            first.Responder.PrivateRepliesDeleted.Should().BeTrue();
            second.Responder.PrivateRepliesDeleted.Should().BeFalse();
        }

        [Test]
        public async Task members_read_only_their_section()
        {
            await AddUser(Alice);

            var run = await Harness.SendAsync(FakeInteraction.Button("whatsnew:u:0.2.0").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(UsersChannel));

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Cards.Single().Text.Should().StartWith("## Requestarr 0.2.0").And.Contain("### For users").And.NotContain("Member cards");
        }

        [Test]
        public async Task read_changes_for_a_version_not_in_the_changelog_says_so()
        {
            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button("whatsnew:a:9.9.9")));

            run.LastReply.Content.Should().Be(DiscordText.VersionNotFound(new Version(9, 9, 9)));
        }
    }
}
