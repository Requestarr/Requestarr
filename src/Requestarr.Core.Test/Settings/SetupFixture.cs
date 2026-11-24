using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Settings
{
    [TestFixture]
    public class SetupFixture : CoreTest<SetupService>
    {
        private static InstanceView Instance(bool enabled) =>
            new(1, new InstanceInput("Sonarr", "Sonarr", "http://sonarr:8989", null, null, null, null, Requestarr.Core.Backends.InstancePurpose.Everything, enabled, false, true, "dub", null, null, null, null), true);

        private void Given(AuthenticationType auth, bool botConfigured, params InstanceView[] instances)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.AuthenticationMethod).Returns(auth);
            Mocker.SetConstant<IDiscordSettingsStore>(new DiscordSettingsStore());
            Mocker.Resolve<IDiscordSettingsStore>().Set(botConfigured ? new DiscordSettings { BotToken = "token", ApplicationId = 42, GuildId = 7 } : null);
            Mocker.GetMock<IInstanceSettingsService>().Setup(i => i.All()).Returns(new List<InstanceView>(instances));
            GivenServers();
        }

        private void GivenServers(params DiscordServer[] servers) =>
            Mocker.GetMock<IDiscordServers>().Setup(s => s.All()).Returns(servers);

        [Test]
        public void a_new_install_needs_every_step()
        {
            Given(AuthenticationType.None, false);

            var status = Subject.Status();

            status.Needed.Should().BeTrue();
            status.AdminAccount.Should().BeFalse();
            status.Discord.Should().BeFalse();
            status.Instance.Should().BeFalse();
            (status.InviteUrl, status.ReadOnlyInviteUrl).Should().Be((null, null));
        }

        [Test]
        public void a_turned_off_instance_doesnt_count()
        {
            Given(AuthenticationType.Forms, true, Instance(enabled: false));

            Subject.Status().Needed.Should().BeTrue();
        }

        [Test]
        public void skipping_the_sonarr_or_radarr_step_leaves_an_install_ready_with_none()
        {
            Given(AuthenticationType.Forms, true);
            Mocker.GetMock<IConfigService>().SetupProperty(c => c.SetupInstanceSkipped);

            Subject.Status().Needed.Should().BeTrue("a sign-in and a bot aren't enough until the step is done or skipped");

            var status = Subject.SkipInstance();

            (status.Needed, status.Instance, status.InstanceSkipped).Should().Be((false, false, true));
        }

        [Test]
        public void an_install_with_a_sign_in_a_bot_and_an_instance_is_ready_with_only_the_read_only_invite()
        {
            Given(AuthenticationType.Forms, true, Instance(enabled: true));
            GivenServers(new DiscordServer("7", "Home", null, true, true));

            var status = Subject.Status();

            status.Needed.Should().BeFalse();
            status.InviteUrl.Should().BeNull("the bot is in Requestarr's server");
            status.ReadOnlyInviteUrl.Should().Contain("client_id=42").And.Contain($"permissions={SetupService.ReadOnlyInvitePermissions}");
        }

        [Test]
        public void a_bot_with_only_its_token_offers_the_full_invite_until_it_has_a_server()
        {
            Given(AuthenticationType.Forms, false, Instance(enabled: true));
            Mocker.Resolve<IDiscordSettingsStore>().Set(new DiscordSettings { BotToken = "token", ApplicationId = 42 });

            var status = Subject.Status();

            (status.Discord, status.PickServer).Should().Be((false, false));
            status.InviteUrl.Should().Contain("client_id=42").And.Contain($"permissions={SetupService.InvitePermissions}");
        }

        [Test]
        public void a_bot_in_several_servers_without_one_chosen_asks_the_admin_to_pick()
        {
            Given(AuthenticationType.Forms, false);
            Mocker.Resolve<IDiscordSettingsStore>().Set(new DiscordSettings { BotToken = "token", ApplicationId = 42 });
            GivenServers(new DiscordServer("1", "Home", null, false, true), new DiscordServer("2", "Old bot", null, false, true));

            var status = Subject.Status();

            status.PickServer.Should().BeTrue();
            status.Servers.Select(s => s.Name).Should().Equal("Home", "Old bot");
        }

        [Test]
        public void a_bot_removed_from_requestarrs_server_offers_the_full_invite_again()
        {
            Given(AuthenticationType.Forms, true);
            GivenServers(new DiscordServer("7", null, null, true, false, false));

            Subject.Status().InviteUrl.Should().Contain($"permissions={SetupService.InvitePermissions}");
        }

        [Test]
        public void a_server_that_removed_the_bot_has_the_invite_that_adds_it_back_there_with_the_same_permissions()
        {
            Given(AuthenticationType.Forms, true);
            GivenServers(new DiscordServer("7", "Home", null, true, false, false), new DiscordServer("8", "Old bot", null, false, false, false), new DiscordServer("9", "Asleep", null, false, false));

            var servers = Subject.Status().Servers.ToDictionary(s => s.Name, s => s.ReconnectUrl);

            servers["Home"].Should().Contain($"permissions={SetupService.InvitePermissions}").And.EndWith("&guild_id=7&disable_guild_select=true");
            servers["Old bot"].Should().Contain($"permissions={SetupService.ReadOnlyInvitePermissions}").And.EndWith("&guild_id=8&disable_guild_select=true");
            servers["Asleep"].Should().BeNull("the bot is still in it");
        }

        [Test]
        public void the_beta_image_shows_this_is_beta()
        {
            Given(AuthenticationType.Forms, true);
            Mocker.GetMock<Requestarr.Core.Announcements.IReleaseEnvironment>().SetupGet(e => e.BetaFromImage).Returns(true);

            Subject.Status().BetaFromImage.Should().BeTrue();
        }

        [Test]
        public void a_server_with_a_provisioning_file_never_opens_the_setup()
        {
            Given(AuthenticationType.None, false);
            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);

            Subject.Status().Needed.Should().BeFalse();
        }
    }
}
