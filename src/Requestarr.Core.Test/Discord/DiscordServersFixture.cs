using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Discord
{
    // The server cards: Discord only says which servers the bot is in now, so Requestarr remembers those it was in, and
    // the admin can add the bot back to one that removed it, or forget it.
    [TestFixture]
    public class DiscordServersFixture : CoreTest<DiscordServers>
    {
        private static readonly DiscordServer Home = new("7", "Home", "home.png", true, true);
        private static readonly DiscordServer Old = new("8", "Old bot", "old.png", false, true);

        [SetUp]
        public void SetUp()
        {
            Mocker.SetConstant<IDiscordSettingsStore>(new DiscordSettingsStore());
            Mocker.Resolve<IDiscordSettingsStore>().Set(new DiscordSettings { BotToken = "token", ApplicationId = 42, GuildId = 7 });
            Mocker.GetMock<IConfigService>().SetupProperty(c => c.DiscordKnownServers);
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.IsOnline).Returns(true);
        }

        private void GivenBotIsIn(params DiscordServer[] servers)
        {
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.Servers).Returns(servers);
            Subject.Handle(new DiscordStatusChangedEvent());
        }

        [Test]
        public void a_server_that_removed_the_bot_keeps_its_card()
        {
            GivenBotIsIn(Home, Old);
            GivenBotIsIn(Home);

            Subject.All().Should().Equal(Home, Old with { Reachable = false, Joined = false });
        }

        [Test]
        public void adding_the_bot_back_leaves_one_card()
        {
            GivenBotIsIn(Home, Old);
            GivenBotIsIn(Home);
            GivenBotIsIn(Home, Old);

            Subject.All().Should().Equal(Home, Old);
        }

        [Test]
        public void requestarrs_own_server_keeps_its_name_when_the_bot_is_removed_from_it()
        {
            GivenBotIsIn(Home);
            GivenBotIsIn(new DiscordServer("7", null, null, true, false, false));

            Subject.All().Should().Equal(Home with { Reachable = false, Joined = false });
        }

        [Test]
        public void nothing_looks_removed_before_the_bot_is_online()
        {
            GivenBotIsIn(Home, Old);
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.IsOnline).Returns(false);
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.Servers).Returns(Array.Empty<DiscordServer>());

            Subject.All().Should().BeEmpty();
        }

        [Test]
        public async Task a_server_the_bot_leaves_is_forgotten()
        {
            GivenBotIsIn(Home, Old);
            Mocker.GetMock<IDiscordClientHost>().Setup(h => h.LeaveServerAsync(8)).ReturnsAsync(true);

            (await Subject.RemoveAsync(8)).Should().BeTrue();

            // Discord says the bot is out a moment later; anything else happening meanwhile doesn't bring the card back.
            GivenBotIsIn(Home, Old);
            GivenBotIsIn(Home);

            Subject.All().Should().Equal(Home);
        }

        [Test]
        public async Task a_server_left_before_can_be_added_again()
        {
            GivenBotIsIn(Home, Old);
            Mocker.GetMock<IDiscordClientHost>().Setup(h => h.LeaveServerAsync(8)).ReturnsAsync(true);
            await Subject.RemoveAsync(8);
            GivenBotIsIn(Home);
            GivenBotIsIn(Home, Old);
            GivenBotIsIn(Home);

            Subject.All().Should().Equal(Home, Old with { Reachable = false, Joined = false });
        }

        [Test]
        public void another_bots_servers_arent_this_ones()
        {
            GivenBotIsIn(Home, Old);
            Mocker.Resolve<IDiscordSettingsStore>().Set(new DiscordSettings { BotToken = "other", ApplicationId = 43, GuildId = 7 });
            GivenBotIsIn(Home);

            Subject.All().Should().Equal(Home);
        }

        [Test]
        public async Task nothing_is_removed_while_the_bot_is_offline()
        {
            GivenBotIsIn(Home, Old);
            GivenBotIsIn(Home);
            Mocker.GetMock<IDiscordClientHost>().SetupGet(h => h.IsOnline).Returns(false);

            (await Subject.RemoveAsync(8)).Should().BeFalse();
        }

        [Test]
        public async Task a_server_that_removed_the_bot_can_be_forgotten()
        {
            GivenBotIsIn(Home, Old);
            GivenBotIsIn(Home);

            (await Subject.RemoveAsync(8)).Should().BeTrue();

            Subject.All().Should().Equal(Home);
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<DiscordStatusChangedEvent>()), Times.Once(), "the open pages drop its card");
            (await Subject.RemoveAsync(8)).Should().BeFalse("nothing is left to forget");
        }

        [Test]
        public async Task requestarrs_own_server_is_never_left_or_forgotten()
        {
            GivenBotIsIn(Home);

            (await Subject.RemoveAsync(7)).Should().BeFalse();

            Mocker.GetMock<IDiscordClientHost>().Verify(h => h.LeaveServerAsync(It.IsAny<ulong>()), Times.Never());
            Subject.All().Single().Name.Should().Be("Home");
        }
    }
}
