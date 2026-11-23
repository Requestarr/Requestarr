using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;
using Requestarr.Test.Common.Categories;

namespace Requestarr.Core.Test.Announcements
{
    // Live: a server that keeps its version cards (staging, production) shows a released version's rewritten words
    // after the update that carries them. On the dev server: a test card for the version running, posted with longer
    // words, is edited in place on the next start, and its Read changes is posted once for real and read back. Everything
    // it posts is removed. Never runs in CI (ManualTest). Needs the dev stack's Requestarr stopped.
    [TestFixture]
    [ManualTest]
    public class LiveVersionWordsFixture
    {
        private const string TestLine = "- **Test card:** the longer words a build had before its lines were rewritten, kept here only for this check.\n";

        [Test]
        public async Task a_kept_card_shows_the_rewritten_words_after_the_next_start()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN not set");
            }

            var settings = new DiscordSettingsStore();
            settings.Set(LiveDev.Settings());
            var guildSetup = new Mock<IGuildSetupService>();
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup.Object),
                new Lazy<IUserManagementService>(() => Mock.Of<IUserManagementService>()),
                new Lazy<IPanelService>(() => Mock.Of<IPanelService>()),
                new Lazy<IRequestFeed>(() => Mock.Of<IRequestFeed>()),
                new Lazy<Requestarr.Core.Access.IAccessRequestService>(() => null),
                new Lazy<Requestarr.Core.Settings.IDiscordServerChoice>(() => null),
                new Lazy<IDiscordAdmins>(() => null),
                Mock.Of<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));
            var gateway = new DiscordNetGateway(host, settings);
            var leftovers = new LiveLeftovers(gateway);

            try
            {
                await host.StartAsync(settings.Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
                var guild = host.Client.GetGuild(settings.Current.GuildId);
                ulong Channel(string name) => guild.TextChannels.Single(c => c.Name == name).Id;
                var admin = Channel(ChannelLayout.AdminChannelName);
                var users = Channel(ChannelLayout.AnnouncementsChannelName);
                await leftovers.NoteAsync(admin, users);

                // The version running is the newest released build; the server has announced it already.
                var shipped = Changelog.Embedded().Replace("\r\n", "\n");
                var version = Changelog.Parse(shipped).Where(v => v.Date != null && v.HasNews).Max(v => v.Version);
                var config = new DiscordGuildConfig { AdminChannelId = admin.ToString(), AnnouncementsChannelId = users.ToString(), AnnouncedVersion = version.ToString() };
                guildSetup.Setup(g => g.GetConfig()).Returns(config);
                guildSetup.SetupGet(g => g.AnnouncementsChannelId).Returns(users);
                var announcements = Announcements(gateway, guildSetup.Object, version);

                // The cards as an earlier build posted them: one more line, so a different main change and count.
                var heading = $"## {Changelog.Display(version)} (";
                var adminsAt = shipped.IndexOf("### For admins\n", shipped.IndexOf(heading, StringComparison.Ordinal), StringComparison.Ordinal) + "### For admins\n".Length;
                var before = shipped.Insert(adminsAt, TestLine);
                var old = Changelog.Parse(before).Single(v => v.Version == version);
                var adminCard = await gateway.SendMessageAsync(admin, VersionCards.Collapsed(old, VersionAudience.Admins, DateTime.UtcNow.Year).Reply);
                var oldWords = Said(await Card(gateway, admin, adminCard));
                TestContext.Progress.WriteLine($"[before] admin card {adminCard}: \"{oldWords}\"");

                // The next start, on the build with the rewritten words.
                announcements.ChangelogText = () => shipped;
                await announcements.PostVersionNotesAsync();
                var after = await Card(gateway, admin, adminCard);
                var fresh = VersionCards.Collapsed(Changelog.Parse(shipped).Single(v => v.Version == version), VersionAudience.Admins, DateTime.UtcNow.Year);
                TestContext.Progress.WriteLine($"[after] admin card {(after == null ? "gone" : after.Id.ToString())}: \"{(after == null ? string.Empty : Said(after))}\"");
                after.Should().NotBeNull("the card is edited in place, not posted again");
                after.EmbedDescription.Should().Be(fresh.Description);
                Said(after).Should().NotBe(oldWords);

                // Read changes for that version: one message, one card a section, as Discord takes it.
                var full = announcements.FullCard(VersionAudience.Admins, version);
                TestContext.Progress.WriteLine($"[read changes] {full.Count} message(s), cards {string.Join(", ", full.Select(r => r.Cards.Count))}, text {string.Join(", ", full.Select(r => r.Cards.Sum(c => c.Text.Length)))} characters");
                full.Should().ContainSingle();
                full[0].Cards.Should().HaveCount(2);
                var posted = await gateway.SendMessageAsync(admin, new InteractionReply { Cards = full[0].Cards });
                var shown = await ((IMessageChannel)await host.Client.Rest.GetChannelAsync(admin)).GetMessageAsync(posted);
                var texts = Texts(shown);
                foreach (var text in texts)
                {
                    TestContext.Progress.WriteLine($"[read changes as Discord shows it]\n{text}");
                }

                texts.Should().HaveCount(2).And.OnlyContain(t => !t.Contains("Test card"));
            }
            finally
            {
                var (removed, left) = await leftovers.RemoveAsync();
                TestContext.Progress.WriteLine($"[cleanup] removed {removed} message(s) the check posted, {left} left");
                await host.StopAsync();
            }
        }

        private static AnnouncementService Announcements(IDiscordGuildGateway gateway, IGuildSetupService guildSetup, Version version)
        {
            // Staging's way of keeping cards: every version for good, each card's words brought up to date on a start.
            var environment = new ReleaseEnvironment(Mock.Of<NzbDrone.Core.Configuration.IConfigService>())
            {
                Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? "staging" : null
            };

            return new AnnouncementService(gateway, environment, guildSetup, Mock.Of<IDiscordGuildConfigRepository>(), Mock.Of<IRequestarrUserRepository>(), Mock.Of<IUserIdentityRepository>(), LogManager.GetLogger("LiveAnnouncements"))
            {
                CurrentVersion = () => new Version(version.Major, version.Minor, version.Build, 0)
            };
        }

        private static async Task<ChannelMessage> Card(IDiscordGuildGateway gateway, ulong channel, ulong id) =>
            (await gateway.GetOwnMessagesAsync(channel, 50)).SingleOrDefault(m => m.Id == id);

        private static string Said(ChannelMessage message) => $"{message.EmbedTitle} | {message.EmbedDescription}";

        private static List<string> Texts(IMessage message) =>
            message.Components.OfType<ContainerComponent>()
                .Select(c => string.Join("\n", c.Components.OfType<TextDisplayComponent>().Select(t => t.Content)))
                .ToList();
    }
}
