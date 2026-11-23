using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Availability
{
    // Help for availability, adding seasons and the feed: the help topics in Discord, the setup guide and the command
    // reference.
    [TestFixture]
    public class AvailabilityHelpFixture : FeedScenario
    {
        [Test]
        public async Task users_get_the_downloads_topic_and_the_seasons_topic_explains_adding()
        {
            var help = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "downloads").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            help.LastReply.Content.Should().StartWith("**Downloads and availability**").And.Contain("every episode that has aired");

            await SetAccess(Alice, "tv", "approval");
            var seasons = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "seasons").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            seasons.LastReply.Content.Should().Contain(DiscordText.AddSeasonsLabel).And.Contain("never removed");
        }

        [Test]
        public async Task admins_get_the_feed_and_downloads_topics()
        {
            var feed = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "feed").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            feed.LastReply.Content.Should().StartWith("**The admin feed**").And.Contain(ChannelLayout.FeedChannelName).And.Contain("never deleted");

            var downloads = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "downloads").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));
            downloads.LastReply.Content.Should().Contain("availability.webhookUrl").And.Contain(Requestarr.Core.Backends.ArrApiBase.WebhookName).And.Contain("pollingOnly");

            HelpCatalog.AdminTopics.Select(t => t.Key).Should().Contain(new[] { "feed", "downloads" });
            HelpCatalog.UserTopics.Concat(HelpCatalog.AdminTopics).Should().OnlyContain(t => t.Intro(HelpViewer.Everything).Length < DiscordLimits.MessageLength - 200);
        }

        [Test]
        public void the_setup_guide_and_the_command_reference_explain_downloads_and_the_feed()
        {
            var guide = File.ReadAllText(RepoFiles.Path("docs", "setup-guide.md"));
            var commands = File.ReadAllText(RepoFiles.Path("docs", "commands.md"));

            guide.Should().Contain("### 4.9 Downloads and availability")
                .And.Contain("webhookUrl: http://requestarr:9797")
                .And.Contain("Docker, Sonarr/Radarr in the same Compose file")
                .And.Contain("Not in Docker, all on one machine")
                .And.Contain("When it can't reach Requestarr")
                .And.Contain("`#requestarr-feed`")
                .And.Contain("`pollingOnly`");
            commands.Should().Contain("⬇️ **Downloading**").And.Contain("**Add seasons**").And.Contain("### Approval cards and the admin feed").And.Contain("never deleted");
        }
    }
}
