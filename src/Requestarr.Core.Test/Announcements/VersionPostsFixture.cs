using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Announcements
{
    // The "what's new" cards are a standing record, kept in step with each environment, each card posted once and
    // never edited or posted again. Production and staging keep every version for good (UpgradePostsFixture). QA
    // shows what staging doesn't have yet, dev only the version running now, and a reset posts them again. The lists
    // below are the versions of the cards in each channel, in the order they were posted.
    [TestFixture]
    public class VersionPostsFixture : DiscordScenario
    {
        private const string Text = @"# Changelog

## 0.3.0

### For admins
- **Faster start:** Requestarr is ready sooner after a restart.

## 0.2.5

## 0.2.0

### For users
- **Follow:** follow a request from its card.

### For admins
- **Member cards:** a clearer layout.

## 0.1.0

### For users
- **Requests:** ask for movies and TV.
";

        // A dev or QA build's changelog: what changed since the last build sent to testers, above the numbered versions.
        private const string Pending = @"# Changelog

## Unreleased

### For admins
- **Build numbers:** each build sent to testers has one.

## 0.6.0 (2026-11-13)

### For admins
- **Cards:** clearer.
";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        private ulong UsersChannel => ulong.Parse(Config.AnnouncementsChannelId);

        private List<string> AdminHeaders => Cards(AdminChannel, VersionAudience.Admins);

        private List<string> UserHeaders => Cards(UsersChannel, VersionAudience.Users);

        private List<string> Cards(ulong channel, VersionAudience audience) =>
            Discord.Channels[channel].Messages.Where(m => m.Embeds.Any()).Select(m => VersionCards.Parse(m.Buttons.Select(b => b.CustomId), audience)?.ToString()).Where(v => v != null).ToList();

        private void Environment(string name, string staged = null)
        {
            var environment = new ReleaseEnvironment(Mock.Of<IConfigService>())
            {
                Variable = v => v switch
                {
                    ReleaseEnvironment.EnvironmentVariable => name,
                    ReleaseEnvironment.StagedVersionsVariable => staged,
                    _ => null
                }
            };
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.VersionPosts).Returns(environment.VersionPosts);
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.Staged).Returns(environment.Staged);
        }

        private async Task StartOn(string version, string text = Text)
        {
            Service.Changelog = () => ChangelogSource.FromMarkdown(text);
            Service.CurrentVersion = () => new Version(version + ".42");
            await Service.PostVersionNotesAsync();
        }

        // A card as an earlier version of Requestarr posted it: the same button, an older title.
        private static InteractionReply OldCard(string version, string audience, string title) => new()
        {
            Embeds = new List<InteractionEmbed> { new() { Title = title } },
            Buttons = new List<InteractionButton> { new() { CustomId = $"{VersionCardRoutes.ReadChanges}:{audience}:{version}", Label = DiscordText.ReadChangesLabel } }
        };

        [TestCase("0.1.0", "0.2.0,0.3.0", "0.2.0")]
        [TestCase(null, "0.1.0,0.2.0,0.3.0", "0.1.0,0.2.0")]
        public async Task qa_shows_every_version_staging_does_not_have_yet(string staged, string admins, string users)
        {
            Environment("qa", staged);

            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(admins.Split(','));
            UserHeaders.Should().Equal(users.Split(','));
        }

        [Test]
        public async Task versions_promoted_to_staging_drop_off_qa()
        {
            Environment("qa");
            await StartOn("0.3.0");

            Environment("qa", "0.1.0,0.2.0,0.2.5");
            await StartOn("0.3.0");

            AdminHeaders.Should().Equal("0.3.0");
            UserHeaders.Should().BeEmpty();

            Environment("qa", "0.1.0,0.2.0,0.2.5,0.3.0");
            await StartOn("0.3.0");

            AdminHeaders.Should().BeEmpty("everything in QA is in staging");
        }

        // reset-qa and reset-dev remove the channels and wipe the database, record included, so everything is posted
        // again; cards gone from the channels while the record is kept are not.
        [TestCase("qa", "0.2.0,0.3.0", "0.2.0")]
        [TestCase("dev", "0.3.0", "")]
        public async Task a_reset_posts_the_cards_again(string environment, string admins, string users)
        {
            Environment(environment, "0.1.0");
            await StartOn("0.3.0");
            DeleteEveryMessage();

            await StartOn("0.3.0");
            AdminHeaders.Should().BeEmpty("a card already posted is never posted again");

            DeleteEveryMessage();
            var config = Config;
            config.AnnouncedVersion = null;
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);
            Mocker.Resolve<IConfigService>().ChangelogPostedEntries = null;
            await StartOn("0.3.0");

            AdminHeaders.Should().Equal(admins.Split(','));
            UserHeaders.Should().Equal(users.Split(',', StringSplitOptions.RemoveEmptyEntries));
        }

        [Test]
        public async Task a_restart_posts_nothing_and_a_new_build_posts_its_card_once()
        {
            Environment("qa");
            await StartOn("0.2.0");
            var ids = Discord.Channels[AdminChannel].MessageIds.ToList();
            var running = Discord.Channels[AdminChannel].Messages[^1].Embeds[0].Title;

            await StartOn("0.2.0");
            Discord.Channels[AdminChannel].MessageIds.Should().Equal(ids, "nothing reposted");

            await StartOn("0.3.0");
            await StartOn("0.3.0");
            AdminHeaders.Should().Equal("0.1.0", "0.2.0", "0.3.0");
            Discord.Channels[AdminChannel].MessageIds.Should().StartWith(ids).And.HaveCount(ids.Count + 1, "the new build's card, once");
            Discord.Channels[AdminChannel].Messages[ids.Count - 1].Embeds[0].Title.Should().Be(running, "the build that was running says something else now, but its card stays as posted");
        }

        [Test]
        public async Task a_version_that_reaches_qa_late_is_posted_after_the_cards_already_there()
        {
            Environment("qa", "0.1.0,0.2.0");
            await StartOn("0.3.0");
            Environment("qa", "0.1.0");

            await StartOn("0.3.0");

            AdminHeaders.Should().Equal("0.3.0", "0.2.0");
        }

        // QA's view of what's coming is one card per QA build: Unreleased under the build's number. A promotion to QA
        // brings a new number, so a new card, and the card of the build it replaces goes.
        [Test]
        public async Task each_qa_build_posts_one_card_for_whats_coming()
        {
            Environment("qa", "0.6.0");
            await StartOn("0.7.4", Pending);
            var first = Discord.Channels[AdminChannel].MessageIds.ToList();

            await StartOn("0.7.5", Pending);
            var second = Discord.Channels[AdminChannel].MessageIds.ToList();
            await StartOn("0.7.5", Pending);

            AdminHeaders.Should().Equal("0.7.5");
            second.Should().NotIntersectWith(first);
            Discord.Channels[AdminChannel].MessageIds.Should().Equal(second);
        }

        [TestCase("qa")]
        [TestCase("dev")]
        public async Task a_builds_card_stays_as_posted_when_its_words_change(string environment)
        {
            Environment(environment, "0.6.0");
            await StartOn("0.7.4", Pending);
            var admin = Discord.Channels[AdminChannel];
            var ids = admin.MessageIds.ToList();
            var said = admin.Messages.Select(m => (m.Embeds[0].Title, m.Embeds[0].Description)).ToList();

            await StartOn("0.7.4", Pending.Replace("- **Build numbers:**", "- **Quieter updates:** each card is posted once.\n- **Build numbers:**"));

            admin.MessageIds.Should().Equal(ids, "never posted again");
            admin.Messages.Select(m => (m.Embeds[0].Title, m.Embeds[0].Description)).Should().Equal(said, "never edited");
            Service.FullCard(VersionAudience.Admins, new Version(0, 7, 4))[0].Cards[0].Text.Should().Contain("Quieter updates", "Read changes shows the new words");
        }

        // Cards posted before QA and dev kept a record are recorded as posted on the first start, not posted again.
        [TestCase("qa")]
        [TestCase("dev")]
        public async Task the_first_start_records_the_cards_already_in_the_channels(string environment)
        {
            Environment(environment, "0.1.0");
            await Discord.SendMessageAsync(AdminChannel, OldCard("0.2.0", "a", "Requestarr 0.2.0 · 2 changes for admins"));
            await Discord.SendMessageAsync(UsersChannel, OldCard("0.2.0", "u", "Requestarr 0.2.0 · 1 new thing for you"));
            var adminIds = Discord.Channels[AdminChannel].MessageIds.ToList();
            var userIds = Discord.Channels[UsersChannel].MessageIds.ToList();

            await StartOn("0.2.0");
            await StartOn("0.2.0");

            Discord.Channels[AdminChannel].MessageIds.Should().Equal(adminIds);
            Discord.Channels[UsersChannel].MessageIds.Should().Equal(userIds);
            Config.AnnouncedVersion.Should().Be("0.2.0");
        }

        private void DeleteEveryMessage()
        {
            foreach (var channel in new[] { AdminChannel, UsersChannel })
            {
                foreach (var id in Discord.Channels[channel].MessageIds.ToList())
                {
                    Discord.DeleteMessage(channel, id);
                }
            }
        }

        [Test]
        public async Task old_style_posts_are_replaced_and_other_messages_left_alone()
        {
            Environment("qa");
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "⚠️ Sonarr (test) can't be reached." });
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "📋 **Requestarr 0.1.0: what's new**\n**For users:** nothing this time, so nothing was posted to users.\n**Internal**\n• x" });
            await Discord.SendMessageAsync(UsersChannel, new InteractionReply { Content = "✨ **What's new in Requestarr**\n• Old." });

            await StartOn("0.3.0");

            Discord.Channels[AdminChannel].Messages.Select(m => m.Content).Should().Contain("⚠️ Sonarr (test) can't be reached.");
            AdminHeaders.Should().Equal("0.1.0", "0.2.0", "0.3.0");
            string.Join("\n", Discord.Channels[AdminChannel].Messages.Select(m => m.Content ?? string.Empty)).Should().NotContain("nothing this time").And.NotContain("Internal");
            UserHeaders.Should().Equal("0.1.0", "0.2.0");
        }

        [Test]
        public async Task dev_shows_only_the_version_running_now()
        {
            Environment("dev");

            await StartOn("0.2.0");
            AdminHeaders.Should().Equal("0.2.0");
            UserHeaders.Should().Equal("0.2.0");

            await StartOn("0.3.0");
            AdminHeaders.Should().Equal("0.3.0");
            UserHeaders.Should().BeEmpty("0.3.0 has nothing for users");

            await StartOn("0.2.5");
            AdminHeaders.Should().BeEmpty("a version with nothing for either posts nothing");
        }

        [Test]
        public async Task staging_never_removes_a_card()
        {
            Environment("staging");
            await Discord.SendMessageAsync(AdminChannel, OldCard("0.0.9", "a", "Requestarr 0.0.9 · 1 change for admins"));

            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminHeaders.Should().Equal("0.0.9", "0.1.0", "0.2.0", "0.3.0");
        }

        [Test]
        public async Task dev_shows_unreleased_as_the_build_it_runs_under_its_label()
        {
            Environment("dev");
            var environment = new ReleaseEnvironment(Mock.Of<IConfigService>()) { Variable = v => v == ReleaseEnvironment.ReleaseVersionVariable ? "0.7.0004-dev.4f2a9c1" : null };
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns<Version>(environment.Shown);

            await StartOn("0.7.4", Pending);

            AdminHeaders.Should().Equal("0.7.4");
            Discord.Channels[AdminChannel].Messages.Single(m => m.Embeds.Any()).Embeds[0].Title.Should().Be("Requestarr 0.7.0004-dev.4f2a9c1", "Unreleased has no day yet");
            Service.FullCard(VersionAudience.Admins, new Version(0, 7, 4))[0].Cards[0].Text.Should().StartWith("## Requestarr 0.7.0004-dev.4f2a9c1\n");
        }

        [Test]
        public async Task qa_shows_unreleased_as_everything_since_the_last_beta_and_a_folded_version_loses_its_card()
        {
            Environment("qa", "0.5.6");
            Mocker.GetMock<IReleaseEnvironment>().Setup(e => e.Shown(It.IsAny<Version>())).Returns<Version>(v => v == new Version(0, 7, 4) ? "0.7.0004-qa.4f2a9c1" : Changelog.Display(v));
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "something else in the channel" });
            await StartOn("0.7.4", Pending + "\n## 0.5.6 (2026-11-13)\n\n### For admins\n- **Testers have it:** yes.\n");
            AdminHeaders.Should().Equal(new[] { "0.6.0", "0.7.4" }, "Unreleased shows with every version staging doesn't have yet");

            // 0.6.0 never reached the testers: it's folded into Unreleased.
            await StartOn("0.7.4", Pending.Replace("## 0.6.0 (2026-11-13)", "## 0.5.6 (2026-11-13)"));

            AdminHeaders.Should().Equal("0.7.4");
            Discord.Channels[AdminChannel].Messages.Single(m => m.Embeds.Any()).Embeds[0].Title.Should().Be("Requestarr 0.7.0004-qa.4f2a9c1 · since the last beta");
            Service.FullCard(VersionAudience.Admins, new Version(0, 7, 4))[0].Cards[0].Text.Should().StartWith("## Requestarr 0.7.0004-qa.4f2a9c1 · since the last beta\n");
            Discord.Channels[AdminChannel].Messages.Should().Contain(m => m.Content == "something else in the channel", "only version cards go");
        }

        [Test]
        public async Task staging_shows_a_build_by_its_four_digit_number_and_never_unreleased()
        {
            Environment("staging");
            var numbered = Pending.Replace("## Unreleased", $"## 0.7.0004 ({DateTime.UtcNow.Year}-11-20)");

            await StartOn("0.6.0", Pending);
            AdminHeaders.Should().Equal("0.6.0");

            await StartOn("0.7.4", numbered);
            AdminHeaders.Should().Equal("0.6.0", "0.7.4");
            Discord.Channels[AdminChannel].Messages.Last(m => m.Embeds.Any()).Embeds[0].Title.Should().Be("Requestarr 0.7.0004 · November 20");
        }

        [TestCase("staging")]
        [TestCase("production")]
        [TestCase("qa")]
        [TestCase("dev")]
        public async Task cards_with_the_old_titles_stay_as_posted(string environment)
        {
            Environment(environment);
            await StartOn("0.2.0");
            var admin = Discord.Channels[AdminChannel];
            var users = Discord.Channels[UsersChannel];
            var adminIds = admin.MessageIds.ToList();
            var userIds = users.MessageIds.ToList();

            // What the server shows after its last start on an earlier version of Requestarr.
            foreach (var (id, message) in admin.MessageIds.Zip(admin.Messages).ToList())
            {
                var version = VersionCards.Parse(message.Buttons.Select(b => b.CustomId), VersionAudience.Admins);
                await Discord.EditMessageAsync(AdminChannel, id, OldCard(version.ToString(), "a", $"Requestarr {version} · 1 change for admins"));
            }

            foreach (var (id, message) in users.MessageIds.Zip(users.Messages).ToList())
            {
                var version = VersionCards.Parse(message.Buttons.Select(b => b.CustomId), VersionAudience.Users);
                await Discord.EditMessageAsync(UsersChannel, id, OldCard(version.ToString(), "u", $"Requestarr {version} · 1 new thing for you"));
            }

            await StartOn("0.2.0");

            admin.MessageIds.Should().Equal(adminIds, "a card already sent stays as it was posted");
            users.MessageIds.Should().Equal(userIds);
            admin.Messages.Should().OnlyContain(m => m.Embeds[0].Title.Contains("change"));
        }

        // A released version's lines rewritten with the owner's approval (0.7.0010, November 23, 2026): the cards the
        // server keeps stay as they were posted, and Read changes shows the new lines.
        [TestCase("staging")]
        [TestCase("production")]
        [TestCase("qa")]
        public async Task a_rewritten_versions_kept_cards_stay_as_posted(string environment)
        {
            Environment(environment);
            var longer = Text.Replace("- **Follow:** follow a request from its card.", "- **Follow:** follow a request from its card, in a long paragraph.\n- **Panels:** your panel, again.");
            await StartOn("0.3.0", longer);
            var admin = Discord.Channels[AdminChannel];
            var users = Discord.Channels[UsersChannel];
            var adminIds = admin.MessageIds.ToList();
            var userIds = users.MessageIds.ToList();
            var before = users.Messages.Select(m => m.Embeds[0].Description).ToList();

            await StartOn("0.3.0");

            admin.MessageIds.Should().Equal(adminIds);
            users.MessageIds.Should().Equal(userIds);
            users.Messages.Select(m => m.Embeds[0].Description).Should().Equal(before);
            Service.FullCard(VersionAudience.Users, new Version(0, 2, 0)).Single().Cards.Single().Text.Should().Contain("- **Follow:** follow a request from its card.").And.NotContain("Panels");
        }

        [Test]
        public async Task staging_replaces_its_posts_from_before_cards_once_keeping_the_history()
        {
            Environment("staging");
            await Discord.SendMessageAsync(AdminChannel, new InteractionReply { Content = "📋 **Requestarr 0.1.0: what's new**\n**For users**\n• Old." });
            await Discord.SendMessageAsync(UsersChannel, new InteractionReply { Content = "✨ **What's new in Requestarr 0.1.0**\n• Old." });
            var config = Config;
            config.AnnouncedVersion = "0.2.0";
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await StartOn("0.2.0");
            var ids = Discord.Channels[AdminChannel].MessageIds.ToList();
            await StartOn("0.2.0");

            AdminHeaders.Should().Equal("0.1.0", "0.2.0");
            UserHeaders.Should().Equal("0.1.0", "0.2.0");
            Discord.Channels[AdminChannel].Messages.Should().NotContain(m => m.Content != null && m.Content.StartsWith("📋"));
            Discord.Channels[AdminChannel].MessageIds.Should().Equal(ids, "converted once");
        }

        [Test]
        public async Task a_long_version_is_kept_as_one_card_across_restarts()
        {
            Environment("qa");
            var lines = string.Join("\n", Enumerable.Range(1, 60).Select(i => $"- **Change {i}:** with enough words to take some room in the message."));
            var text = $"## 0.9.0\n\n### For admins\n{lines}\n";
            await StartOn("0.9.0", text);
            var ids = Discord.Channels[AdminChannel].MessageIds.ToList();

            await StartOn("0.9.0", text);

            Discord.Channels[AdminChannel].MessageIds.Should().Equal(ids);
            AdminHeaders.Should().ContainSingle("a long version is still one card").Which.Should().Be("0.9.0");
        }
    }
}
