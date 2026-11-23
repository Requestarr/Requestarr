using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Portal;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;

namespace Requestarr.Core.Test.Settings
{
    [TestFixture]
    public class AppSettingsFixture : CoreTest<AppSettingsService>
    {
        private static readonly Actor Admin = Actor.Web("owner");

        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupAllProperties();
            Mocker.GetMock<IConfigService>().Object.RemovalVoteDays = 7;
            Mocker.SetConstant<IDiscordSettingsStore>(new DiscordSettingsStore());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());
            Mocker.SetConstant<IStoredSettings>(Mocker.Resolve<StoredSettings>());
            Mocker.SetConstant<IAuditSettings>(Mocker.Resolve<AuditSettings>());
        }

        private Task<SettingsSaveResult> Save(string key, string value) => Subject.SaveAsync(new Dictionary<string, string> { [key] = value }, Admin);

        [Test]
        public async Task should_refuse_changes_where_the_provisioning_file_sets_the_settings()
        {
            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);

            var result = await Save("removalVoteDays", "5");

            result.Saved.Should().BeFalse();
            Subject.Get().Provisioned.Should().BeTrue();
            Mocker.GetMock<IConfigService>().Object.RemovalVoteDays.Should().Be(7);
        }

        [Test]
        public async Task should_save_a_change_and_record_who_made_it_and_what_it_was()
        {
            (await Save("removalVoteDays", "5")).Saved.Should().BeTrue();

            Subject.Get().Values["removalVoteDays"].Should().Be(5);
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, AppSettingsService.ChangeAction, "settings", "removalVoteDays", "Removal vote length (days): from 7 to 5"), Times.Once());
        }

        [Test]
        public async Task should_keep_the_audit_log_for_a_year_until_another_choice_is_saved()
        {
            Subject.Get().Values["auditLogKeepFor"].Should().Be(AuditRetention.OneYear);

            (await Save("auditLogKeepFor", AuditRetention.NinetyDays)).Saved.Should().BeTrue();

            Subject.Get().Values["auditLogKeepFor"].Should().Be(AuditRetention.NinetyDays);
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, AppSettingsService.ChangeAction, "settings", "auditLogKeepFor", "Keep the audit log for: from 1 year to 90 days"), Times.Once());
        }

        [Test]
        public async Task should_start_with_deleting_files_off_and_record_turning_it_on()
        {
            Subject.Get().Values["removalDeleteFiles"].Should().Be(false);

            (await Save("removalDeleteFiles", "true")).Saved.Should().BeTrue();

            Mocker.GetMock<IConfigService>().Object.RemovalDeleteFiles.Should().BeTrue();
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, AppSettingsService.ChangeAction, "settings", "removalDeleteFiles", "Delete files in media managers: from off to on"), Times.Once());
        }

        [Test]
        public async Task should_record_nothing_when_a_value_is_unchanged()
        {
            await Save("removalVoteDays", "7");

            Mocker.GetMock<IAuditService>().Verify(a => a.Record(It.IsAny<Actor>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }

        [TestCase("removalVoteDays", "0")]
        [TestCase("removalVoteDays", "10001")]
        [TestCase("tmdbApiKey", "key")]
        [TestCase("discordGuildId", "my server")]
        [TestCase("availabilityWebhookUrl", "requestarr:9797")]
        [TestCase("bugReportsGithubToken", "token")]
        [TestCase("nonsense", "1")]
        [TestCase("auditLogKeepFor", "3 weeks")]
        [TestCase("serverRole", "staging")]
        public async Task should_refuse_a_value_it_cant_use_and_change_nothing(string key, string value)
        {
            var result = await Subject.SaveAsync(new Dictionary<string, string> { ["accessBlockClicks"] = "9", [key] = value }, Admin);

            result.Saved.Should().BeFalse();
            result.Problem.Should().NotBeNullOrEmpty();
            Mocker.GetMock<IConfigService>().Object.AccessBlockClicks.Should().Be(0);
        }

        [Test]
        public async Task should_keep_a_secret_hidden_and_reconnect_the_bot_when_its_connection_changes()
        {
            await Subject.SaveAsync(new Dictionary<string, string> { ["discordBotToken"] = "token", ["discordApplicationId"] = "123456789012345678", ["discordGuildId"] = "223456789012345678" }, Admin);

            Mocker.Resolve<IDiscordSettingsStore>().Current.IsConfigured.Should().BeTrue();
            Subject.Get().Values["discordBotToken"].Should().Be("••••••••••••en", "a saved token shows only its last characters");
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, AppSettingsService.ChangeAction, "settings", "discordBotToken", "Bot token: set"), Times.Once());
            Mocker.GetMock<IDiscordClientHost>().Verify(h => h.RestartAsync(), Times.Once());
        }

        [Test]
        public async Task should_not_reconnect_the_bot_for_other_settings()
        {
            await Save("removalVoteDays", "5");

            Mocker.GetMock<IConfigService>().Object.RemovalVoteDays.Should().Be(5);
            Mocker.GetMock<IDiscordClientHost>().Verify(h => h.RestartAsync(), Times.Never());
        }

        [Test]
        public async Task should_set_up_the_webhooks_again_when_their_address_changes()
        {
            await Save("availabilityWebhookUrl", "http://requestarr:9797");

            Mocker.Resolve<IAvailabilitySettingsStore>().WebhookUrl.Should().Be("http://requestarr:9797");
            Mocker.GetMock<IBackendService>().Verify(b => b.CheckAllAsync(), Times.Once());
        }

        [Test]
        public async Task should_keep_the_saved_token_when_its_masked_form_comes_back()
        {
            await Save("discordBotToken", "abcdefghijklmnop");
            var shown = AppSettingsService.Masked("abcdefghijklmnop");

            shown.Should().Be("••••••••••••mnop");
            (await Save("discordBotToken", shown)).Saved.Should().BeTrue();
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, AppSettingsService.ChangeAction, "settings", "discordBotToken", It.IsAny<string>()), Times.Once());

            Mocker.GetMock<IConfigService>().Object.DiscordBotToken.Should().Be("abcdefghijklmnop");
        }

        [Test]
        public void should_name_a_web_admin_in_timelines()
        {
            PortalText.Actor(Admin.Description).Should().Be("owner (web app)");
            PortalText.Actor(Actor.Web(null).Description).Should().Be("An admin (web app)");
        }
    }
}
