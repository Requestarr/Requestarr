using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Announcements
{
    // Outages: the admin decides what users hear. Still down after 30 minutes, the down alert offers "Announce to
    // users"; an announced outage has its recovery announced; one never announced is never announced. The alert (and
    // the notice to users, if announced) is pinned while the outage lasts; on recovery both are unpinned and removed and
    // the "back" messages are posted, unpinned. Every outage is kept in Requestarr's own record.
    [TestFixture]
    public class OutageAnnouncementFixture : RequestMessageScenario
    {
        private static readonly DateTime Start = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);
        private DateTime _now;

        private ConnectionMonitor Monitor => (ConnectionMonitor)Mocker.Resolve<IConnectionMonitor>();

        private BackendInstance Sonarr1 => Instance("Sonarr");

        private FakeDiscordGuild.FakeChannel AdminRoom => Discord.Channels[AdminChannel];

        private FakeDiscordGuild.FakeChannel UsersRoom => Discord.Channels[ulong.Parse(Config.AnnouncementsChannelId)];

        private InteractionReply Alert(string instance) => AdminRoom.Messages.SingleOrDefault(m => m.Content != null && m.Content.StartsWith($"⚠️ **{instance} can't be reached**"));

        private InteractionReply DownAlert => Alert("Sonarr");

        private ulong AlertId(string instance) => AdminRoom.MessageIds[AdminRoom.Messages.IndexOf(Alert(instance))];

        [SetUp]
        public void Clock()
        {
            _now = Start;
            Monitor.Clock = () => _now;
        }

        private async Task Minutes(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _now += TimeSpan.FromMinutes(1);
                await Monitor.CheckAllAsync();
            }
        }

        private async Task DownFor(int minutes)
        {
            Sonarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(minutes);
        }

        private async Task Back()
        {
            Sonarr.Unreachable = false;
            await Minutes(1);
        }

        private Task<InteractionRun> PressAnnounce() =>
            Harness.SendAsync(AsAdmin(FakeInteraction.Button(DownAlert.Buttons.Single().CustomId)));

        [Test]
        public async Task the_button_appears_only_after_thirty_minutes()
        {
            await DownFor(29);
            DownAlert.Buttons.Should().BeNullOrEmpty("the alert itself comes at 3 minutes; the button only at 30");

            await Minutes(1);

            DownAlert.Buttons.Single().Label.Should().Be(DiscordText.AnnounceLabel);
            DownAlert.Buttons.Single().CustomId.Should().Be($"{ConnectionRoutes.Announce}:{Sonarr1.Id}");
            DownAlert.Content.Should().Contain(DiscordText.AnnounceOffer(TimeSpan.FromMinutes(30)));
            UsersRoom.Messages.Should().BeEmpty("nothing reaches users until an admin decides");
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task an_announced_outage_pins_a_plain_notice_and_its_recovery_is_announced_and_recorded()
        {
            await DownFor(31);

            var run = await PressAnnounce();

            run.LastReply.Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be(DiscordText.Announced("Sonarr"));
            UsersRoom.Messages.Single().Content.Should().Be(DiscordText.OutageNotice(ContentType.Tv, false));
            UsersRoom.Messages.Single().Content.Should().StartWith("⚠️ **TV requests are paused**").And.Contain("The admin knows").And.NotContain("Sonarr", "plain words for users");
            UsersRoom.Pinned.Should().ContainSingle().Which.Should().Be(UsersRoom.MessageIds.Single(), "the notice is pinned while the outage lasts");
            DownAlert.Buttons.Should().BeNullOrEmpty("it can't be announced twice");
            DownAlert.Content.Should().Contain(DiscordText.AlertAnnounced("owner"));
            (await Harness.SendAsync(AsAdmin(FakeInteraction.Button($"{ConnectionRoutes.Announce}:{Sonarr1.Id}")))).LastReply.Content.Should().Be(DiscordText.AlreadyAnnounced("Sonarr"));

            await Back();

            UsersRoom.Messages.Should().ContainSingle("the outage notice goes once it's over");
            UsersRoom.Messages.Single().Content.Should().Be(DiscordText.OutageOverNotice(ContentType.Tv, TimeSpan.FromMinutes(32)));
            UsersRoom.Messages.Single().Content.Should().StartWith("✅ **TV requests work again**");
            UsersRoom.Pinned.Should().BeEmpty("the \"back\" message isn't pinned");
            DownAlert.Should().BeNull("the alert goes too");
            AdminRoom.Pinned.Should().BeEmpty();
            AdminRoom.Messages.Last().Content.Should().StartWith("✅ **Sonarr is back**");
            Sonarr1.DownAnnounceMessageId.Should().BeNull();
            Sonarr1.DownAnnounceOffered.Should().BeFalse();

            var outage = Mocker.Resolve<IOutageRepository>().History().Single();
            (outage.InstanceName, outage.Started, outage.Ended, outage.Duration, outage.Announced, outage.AnnouncedBy)
                .Should().Be(("Sonarr", Start, Start.AddMinutes(32), TimeSpan.FromMinutes(32), Start.AddMinutes(31), "owner"));
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task an_outage_never_announced_pins_only_its_alert_and_is_never_announced_on_recovery()
        {
            await DownFor(45);
            DownAlert.Buttons.Should().ContainSingle();
            AdminRoom.Pinned.Should().ContainSingle().Which.Should().Be(AlertId("Sonarr"), "the alert is pinned while it lasts");

            await Back();

            UsersRoom.Messages.Should().BeEmpty();
            DownAlert.Should().BeNull("the alert (and its offer) goes once it's back");
            AdminRoom.Pinned.Should().BeEmpty();
            AdminRoom.Messages.Last().Content.Should().StartWith("✅ **Sonarr is back**");
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task the_next_outage_starts_fresh()
        {
            await DownFor(31);
            await PressAnnounce();
            await Back();

            await DownFor(5);

            AdminRoom.Messages.Last().Content.Should().StartWith("⚠️ **Sonarr can't be reached**");
            AdminRoom.Messages.Last().Buttons.Should().BeNullOrEmpty();
            UsersRoom.Messages.Should().ContainSingle("the earlier \"back\" notice; no new announcement until an admin presses it again");
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task pressing_it_after_recovery_announces_nothing()
        {
            await DownFor(31);
            var stale = DownAlert.Buttons.Single().CustomId;
            await Back();

            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.Button(stale)));

            run.LastReply.Content.Should().Be(DiscordText.AnnounceNotDown("Sonarr"));
            UsersRoom.Messages.Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task only_admins_can_announce()
        {
            await DownFor(31);

            var run = await Harness.SendAsync(FakeInteraction.Button(DownAlert.Buttons.Single().CustomId).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            UsersRoom.Messages.Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task several_outages_each_have_their_own_pinned_alert()
        {
            Sonarr.Unreachable = true;
            Radarr.Unreachable = true;
            await Monitor.CheckAllAsync();
            await Minutes(4);

            AdminRoom.Pinned.Should().HaveCount(3, "Sonarr, Radarr and Radarr 4K");
            Mocker.Resolve<IOutageRepository>().History().Should().HaveCount(3).And.OnlyContain(o => o.Ended == null && o.Announced == null);

            Sonarr.Unreachable = false;
            await Minutes(1);

            AdminRoom.Pinned.Should().HaveCount(2);
            Mocker.Resolve<IOutageRepository>().History().Count(o => o.Ended != null).Should().Be(1);
            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task a_blip_shorter_than_the_alert_is_not_an_outage()
        {
            await DownFor(1);
            await Back();

            Mocker.Resolve<IOutageRepository>().History().Should().BeEmpty();
            AdminRoom.Pinned.Should().BeEmpty();
        }

        [Test]
        public async Task without_pin_messages_the_alert_still_comes_unpinned()
        {
            Discord.CannotPin = true;
            await DownFor(4);

            DownAlert.Should().NotBeNull();
            AdminRoom.Pinned.Should().BeEmpty();
            ExceptionVerification.IgnoreWarns();
        }

        [TestCase(20, "1 minute")]
        [TestCase(60, "1 minute")]
        [TestCase(1800, "30 minutes")]
        public void durations_read_naturally(int seconds, string expected)
        {
            DiscordText.Duration(TimeSpan.FromSeconds(seconds)).Should().Be(expected);
        }
    }
}
