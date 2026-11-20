using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;

namespace Requestarr.Core.Test.Settings
{
    [TestFixture]
    public class SetupFixture : CoreTest<SetupService>
    {
        private static InstanceView Instance(bool enabled) =>
            new(1, new InstanceInput("Sonarr", "Sonarr", "http://sonarr:8989", null, null, null, null, true, enabled, false, true, "dub", null, null, null, null), true, true, null);

        private void Given(AuthenticationType auth, bool botConfigured, params InstanceView[] instances)
        {
            Mocker.GetMock<IConfigFileProvider>().SetupGet(c => c.AuthenticationMethod).Returns(auth);
            Mocker.SetConstant<IDiscordSettingsStore>(new DiscordSettingsStore());
            Mocker.Resolve<IDiscordSettingsStore>().Set(botConfigured ? new DiscordSettings { BotToken = "token", ApplicationId = 42, GuildId = 7 } : null);
            Mocker.GetMock<IInstanceSettingsService>().Setup(i => i.All()).Returns(new List<InstanceView>(instances));
        }

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
        public void an_install_with_a_sign_in_a_bot_and_an_instance_is_ready_with_both_invite_links()
        {
            Given(AuthenticationType.Forms, true, Instance(enabled: true));

            var status = Subject.Status();

            status.Needed.Should().BeFalse();
            status.InviteUrl.Should().Contain("client_id=42").And.Contain($"permissions={SetupService.InvitePermissions}");
            status.ReadOnlyInviteUrl.Should().Contain("client_id=42").And.Contain($"permissions={SetupService.ReadOnlyInvitePermissions}");
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
