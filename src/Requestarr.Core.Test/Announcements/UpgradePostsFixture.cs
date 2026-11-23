using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Announcements
{
    // On a new version, a card to the admin channel and, when it has lines for users, to the announcements channel:
    // once per version, across restarts, in order. Production and staging (the default here) keep every post and
    // start with the full history; how the other environments keep them: VersionPostsFixture.
    [TestFixture]
    public class UpgradePostsFixture : DiscordScenario
    {
        private const string Text = @"# Changelog

## 0.3.0

### For admins
- Faster startup.

## 0.2.5

## 0.2.0

### For users
- You can now follow a request from its card.
- Your panel shows your limits.

### For admins
- New member card layout.

## 0.1.0

### For users
- Request movies and TV.
";

        private AnnouncementService Service => (AnnouncementService)Mocker.Resolve<IAnnouncementService>();

        // The version cards' titles, oldest first.
        private List<string> AdminPosts => Titles(AdminChannel);

        private List<string> UserPosts => Titles(ulong.Parse(Config.AnnouncementsChannelId));

        private List<string> Titles(ulong channel) =>
            Discord.Channels[channel].Messages.Where(m => m.Embeds.Any()).Select(m => m.Embeds[0].Title).Where(t => t != null && t.StartsWith("Requestarr ")).ToList();

        // A card's title is its version and the day it was made; this changelog's headings have no date.
        private static string Card(string version) => $"Requestarr {version}";

        private async Task StartOn(string version)
        {
            Service.ChangelogText = () => Text;
            Service.CurrentVersion = () => new Version(version + ".123");
            await Service.PostVersionNotesAsync();
        }

        [Test]
        public async Task the_first_start_posts_the_full_history_oldest_first()
        {
            Config.AnnouncedVersion.Should().BeNull("a new server");

            await StartOn("0.3.0");

            AdminPosts.Should().Equal(Card("0.1.0"), Card("0.2.0"), Card("0.3.0"));
            UserPosts.Should().Equal(Card("0.1.0"), Card("0.2.0"));
        }

        [Test]
        public async Task each_version_is_announced_once_even_across_restarts()
        {
            await StartOn("0.2.0");
            await StartOn("0.2.0");
            await Mocker.Resolve<IGuildSetupService>().EnsureAsync();
            await StartOn("0.2.0");

            AdminPosts.Should().HaveCount(2, "0.1.0 and 0.2.0, once each");
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task a_version_with_nothing_for_users_posts_nothing_to_users_and_no_users_section()
        {
            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminPosts.Should().HaveCount(3);
            AdminPosts.Last().Should().Be(Card("0.3.0"));
            UserPosts.Should().HaveCount(2, "0.3.0 has nothing for users");
            Config.AnnouncedVersion.Should().Be("0.1.0,0.2.0,0.2.5,0.3.0");
        }

        [Test]
        public async Task a_version_with_nothing_for_anyone_posts_nothing_anywhere()
        {
            await StartOn("0.2.0");
            var admin = AdminPosts.Count;
            var users = UserPosts.Count;

            await StartOn("0.2.5");

            (AdminPosts.Count, UserPosts.Count).Should().Be((admin, users));
            Config.AnnouncedVersion.Should().Contain("0.2.5", "recorded, so it never posts later");
            string.Join("\n", AdminPosts.Concat(UserPosts)).Should().NotContain("nothing this time").And.NotContain("Internal");
        }

        [Test]
        public async Task skipped_versions_are_each_announced_in_order()
        {
            await StartOn("0.1.0");
            await StartOn("0.3.0");

            AdminPosts.Should().Equal(Card("0.1.0"), Card("0.2.0"), Card("0.3.0"));
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task a_server_set_up_again_with_its_channels_kept_does_not_repeat_the_history()
        {
            await StartOn("0.2.0");
            var config = Config;
            config.AnnouncedVersion = null;
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await StartOn("0.3.0");

            AdminPosts.Should().Equal(Card("0.1.0"), Card("0.2.0"), Card("0.3.0"));
            UserPosts.Should().HaveCount(2);
        }

        [Test]
        public async Task an_older_build_never_reposts()
        {
            await StartOn("0.3.0");
            await StartOn("0.2.0");
            await StartOn("0.3.0");

            AdminPosts.Should().HaveCount(3, "0.1.0, 0.2.0 and 0.3.0, once each");
        }

        // A fix made in QA takes the next number above dev's, so the dev versions it skipped reach QA later.
        [Test]
        public async Task versions_a_qa_fix_skipped_over_are_announced_when_they_arrive()
        {
            const string qa = @"## 0.2.6

### For users
- The fix.

## 0.2.3

### For admins
- Promoted.
";
            const string promoted = @"## 0.2.7

### For admins
- Newer.

## 0.2.6

### For users
- The fix.

## 0.2.5

### For users
- Five.

## 0.2.4

### For admins
- Four.

## 0.2.3

### For admins
- Promoted.
";

            Service.ChangelogText = () => qa;
            Service.CurrentVersion = () => new Version(0, 2, 3, 1);
            await Service.PostVersionNotesAsync();
            Service.CurrentVersion = () => new Version(0, 2, 6, 1);
            await Service.PostVersionNotesAsync();
            Service.ChangelogText = () => promoted;
            Service.CurrentVersion = () => new Version(0, 2, 7, 1);
            await Service.PostVersionNotesAsync();
            await Service.PostVersionNotesAsync();

            AdminPosts.Select(t => t.Split(' ')[1]).Should().Equal("0.2.3", "0.2.6", "0.2.4", "0.2.5", "0.2.7");
            UserPosts.Should().Equal(Card("0.2.6"), Card("0.2.5"));
            Config.AnnouncedVersion.Should().Be("0.2.3,0.2.4,0.2.5,0.2.6,0.2.7");
        }

        [Test]
        public async Task a_record_from_before_the_list_counts_everything_up_to_it_as_announced()
        {
            var config = Config;
            config.AnnouncedVersion = "0.2.0";
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await StartOn("0.3.0");

            AdminPosts.Should().ContainSingle().Which.Should().Be(Card("0.3.0"));
            Config.AnnouncedVersion.Should().Be("0.2.0,0.2.5,0.3.0");
        }

        [Test]
        public void the_announced_list_reads_and_writes_versions()
        {
            var announced = Announced.Parse("0.2.3, 0.2.6,junk");

            announced.Contains(new Version(0, 1, 0)).Should().BeTrue("everything up to the lowest entry");
            announced.Contains(new Version(0, 2, 6)).Should().BeTrue();
            announced.Contains(new Version(0, 2, 4)).Should().BeFalse("skipped by the fix");
            announced.With(new[] { new Version(0, 2, 4) }).ToString().Should().Be("0.2.3,0.2.4,0.2.6");
            Announced.Parse(null).IsEmpty.Should().BeTrue();
        }

        [Test]
        public async Task nothing_is_marked_announced_when_discord_refuses_the_post()
        {
            Discord.Channels.Remove(AdminChannel);

            await StartOn("0.2.0");

            Config.AnnouncedVersion.Should().BeNull("tried again on the next start");
            Requestarr.Test.Common.ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task a_long_version_is_one_card_and_its_full_card_splits_within_discord_limits()
        {
            var lines = string.Join("\n", Enumerable.Range(1, 200).Select(i => $"- Change number {i} with enough words to take some room in the message."));
            Service.ChangelogText = () => $"## 0.9.0 (2026-10-25)\n\n### For users\n{lines}\n\n### For admins\n{lines}\n";
            Service.CurrentVersion = () => new Version(0, 9, 0, 1);

            await Service.PostVersionNotesAsync();

            AdminPosts.Should().Equal("Requestarr 0.9.0000 · October 25" + (DateTime.UtcNow.Year == 2026 ? string.Empty : ", 2026"));
            var full = Service.FullCard(VersionAudience.Admins, new Version(0, 9, 0));
            full.Should().HaveCountGreaterThan(1, "more than 4,000 characters takes more than one message");
            full[0].Cards[0].Text.Should().StartWith("## Requestarr 0.9.0000 · 25 October 2026\n### For users\n");

            foreach (var reply in full)
            {
                reply.Cards.Count.Should().BeLessOrEqualTo(VersionCards.CardsPerMessage);
                reply.Cards.Sum(c => c.Text.Length).Should().BeLessOrEqualTo(VersionCards.MessageTextLength);
            }

            var text = full.SelectMany(r => r.Cards).Select(c => c.Text).ToList();
            text.SelectMany(t => t.Split('\n')).Where(l => l.StartsWith("- ")).Should().HaveCount(400, "every line, none cut");
            text.Count(t => t.StartsWith("### For users (continued)")).Should().BeGreaterThan(0);
        }
    }
}
