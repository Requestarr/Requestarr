using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Anime;
using Requestarr.Core.Discord.Help;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Anime
{
    // Help explains sub or dub: users with TV access get how the language is chosen, admins how it's set up and ranked.
    [TestFixture]
    public class AnimeHelpFixture : AnimeScenario
    {
        [Test]
        public async Task users_with_tv_get_the_anime_help_topic()
        {
            var help = await Harness.SendAsync(UserCommand("help", Alice));
            help.LastReply.SelectMenu.Options.Select(o => o.Value).Should().Contain("anime");

            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:user", "anime").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            topic.LastReply.Content.Should().StartWith("**Anime: sub or dub**").And.Contain("keep the English dub, keep the subtitled version, or whoever asked first").And.Contain("episodes already downloaded stay");

            await SetAccess(Alice, "tv", "off");
            (await Harness.SendAsync(UserCommand("help", Alice))).LastReply.SelectMenu.Options.Select(o => o.Value).Should().NotContain("anime");
        }

        [Test]
        public async Task admins_get_the_anime_setup_and_priority_topic()
        {
            var topic = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "anime").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            topic.LastReply.Content.Should().StartWith("**Anime, sub/dub and priority**")
                .And.Contain(AnimeDefaults.DubTag).And.Contain(AnimeDefaults.SubProfile)
                .And.Contain("anime.priority").And.Contain("different root folders");
            HelpCatalog.AdminTopics.Select(t => t.Key).Should().Contain("anime");
        }
    }
}
