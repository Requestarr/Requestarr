using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Availability
{
    // How availability is set up: Requestarr's own webhook connection on each instance, each with its own token, the
    // address it calls and the backup poll's interval, and what a webhook may change.
    [TestFixture]
    public class AvailabilitySetupFixture : FeedScenario
    {
        [TestCase("http://requestarr:9797", null, true)]
        [TestCase("https://requests.example/requestarr", 30, true)]
        [TestCase("requestarr:9797", null, false)]
        [TestCase("ftp://requestarr", null, false)]
        [TestCase("http://requestarr:9797?token=x", null, false)]
        [TestCase(null, 4, false)]
        public void the_availability_block_is_checked(string url, int? minutes, bool valid)
        {
            var errors = ProvisioningLoader.Validate(new ProvisioningDocument { Availability = new AvailabilityProvisioning { WebhookUrl = url, PollMinutes = minutes } }).ToList();

            errors.Any(e => e.StartsWith("availability.")).Should().Be(!valid);
        }

        [Test]
        public void the_poll_interval_defaults_to_15_minutes_and_never_goes_below_5()
        {
            var store = new AvailabilitySettingsStore();
            store.Set(" http://requestarr:9797/ ", null);
            store.WebhookUrl.Should().Be("http://requestarr:9797");
            store.PollMinutes.Should().Be(15);

            store.Set(null, 1);
            store.WebhookUrl.Should().BeNull("no address: polling only");
            store.PollMinutes.Should().Be(5);
        }

        [Test]
        public void each_instance_gets_its_own_token_and_a_new_api_key_changes_them_all()
        {
            var key = string.Concat(Enumerable.Repeat("cd", 16));
            var other = string.Concat(Enumerable.Repeat("ef", 16));

            WebhookTokens.For(key, 1).Should().HaveLength(32).And.NotBe(WebhookTokens.For(key, 2));
            WebhookTokens.For(other, 1).Should().NotBe(WebhookTokens.For(key, 1));
            WebhookTokens.Matches(key, 1, WebhookTokens.For(key, 1)).Should().BeTrue();
            WebhookTokens.Matches(key, 1, WebhookTokens.For(key, 2)).Should().BeFalse();
            WebhookTokens.Url("http://requestarr:9797/", key, 3).Should().Be($"http://requestarr:9797/api/v1/webhook/3?token={WebhookTokens.For(key, 3)}");
        }

        [Test]
        public async Task a_webhook_with_a_wrong_token_or_instance_changes_nothing()
        {
            var request = await SentMovie();
            Radarr.Imported(101);

            (await Availability.HandleWebhookAsync(request.BackendId.Value, "wrong", "Download", null, Dune)).Should().Be(WebhookOutcome.Unauthorized);
            (await Availability.HandleWebhookAsync(request.BackendId.Value, null, "Download", null, Dune)).Should().Be(WebhookOutcome.Unauthorized);
            (await Availability.HandleWebhookAsync(999, Token(999), "Download", null, Dune)).Should().Be(WebhookOutcome.UnknownInstance);
            (await Availability.HandleWebhookAsync(Instance("Radarr 4K").Id, Token(request.BackendId.Value), "Download", null, Dune)).Should().Be(WebhookOutcome.Unauthorized, "each instance has its own token");

            Reload(request.Id).Status.Should().Be(RequestStatus.Approved);
            ExceptionVerification.ExpectedWarns(3);
        }

        [Test]
        public async Task webhooks_for_titles_with_no_sent_request_are_ignored()
        {
            await PendingMovie(Alice, Dune);
            var radarrId = Instance("Radarr").Id;

            (await Availability.HandleWebhookAsync(radarrId, Token(radarrId), "Download", null, Dune)).Should().Be(WebhookOutcome.Ignored);
            (await Availability.HandleWebhookAsync(radarrId, Token(radarrId), "Test", null, null)).Should().Be(WebhookOutcome.Accepted);
            Reload(1).Status.Should().Be(RequestStatus.Pending);
        }

        [Test]
        public async Task the_webhook_is_created_once_per_instance_and_never_touches_owner_connections()
        {
            Mocker.Resolve<IAvailabilitySettingsStore>().Set("http://requestarr:9797/", null);

            await Availability.SetUpWebhooksAsync();
            await Availability.SetUpWebhooksAsync();

            var radarr = Instance("Radarr");
            Radarr.Webhooks["http://radarr-test:7878"].Should().Be($"http://requestarr:9797/api/v1/webhook/{radarr.Id}?token={Token(radarr.Id)}");
            Radarr.Webhooks.Should().HaveCount(2, "Radarr and Radarr 4K, each with its own URL");
            Sonarr.Webhooks.Should().ContainKey("http://sonarr-test:8989");
            Radarr.WebhookWrites.Should().Be(2, "the second run changed nothing");
            Radarr.OwnerConnections.Should().Equal("Owner's Discord", "Owner's webhook");
            Availability.WebhookProblems.Should().BeEmpty();
        }

        [Test]
        public async Task polling_only_instances_and_installs_without_an_address_get_no_webhook()
        {
            Mocker.Resolve<IAvailabilitySettingsStore>().Set("http://requestarr:9797", null);
            await Availability.SetUpWebhooksAsync();

            var sonarr = Instance("Sonarr");
            sonarr.Config.PollingOnly = true;
            Mocker.Resolve<IBackendRepository>().Update(sonarr);
            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);
            await Availability.SetUpWebhooksAsync();
            Sonarr.Webhooks.Should().BeEmpty("a provisioning file's polling only removes Requestarr's webhook");
            Availability.UpdatesFor(Instance("Sonarr")).Reason.Should().Be(UpdatesReasons.SetByFile);

            Mocker.Resolve<IAvailabilitySettingsStore>().Set(null, null);
            await Availability.SetUpWebhooksAsync();
            Radarr.Webhooks.Should().BeEmpty();
        }

        [Test]
        public async Task an_instance_saved_as_timer_only_switches_to_right_away_once_its_connect_test_reaches_requestarr()
        {
            Mocker.Resolve<IAvailabilitySettingsStore>().Set("http://requestarr:9797", 10);
            var sonarr = Instance("Sonarr");
            sonarr.Config.PollingOnly = true;
            Mocker.Resolve<IBackendRepository>().Update(sonarr);
            Sonarr.WebhookUnreachable = true;

            await Availability.SetUpWebhooksAsync();

            Sonarr.Webhooks.Should().BeEmpty("it can't reach Requestarr yet, so it stays on the timer");
            Instance("Sonarr").Config.PollingOnly.Should().BeTrue();

            Sonarr.WebhookUnreachable = false;
            await Availability.SetUpWebhooksAsync();

            Instance("Sonarr").Config.PollingOnly.Should().BeFalse("its own Connect test got through");
            Sonarr.Webhooks.Should().ContainKey("http://sonarr-test:8989");
            Availability.UpdatesFor(Instance("Sonarr")).Should().Be(new InstanceUpdates(true, UpdatesReasons.RightAway, 10));
        }

        [Test]
        public async Task each_instance_says_how_its_downloads_are_reported_and_an_address_is_tested_before_saving()
        {
            Mocker.Resolve<IAvailabilitySettingsStore>().Set(null, 10);
            Availability.UpdatesFor(Instance("Radarr")).Reason.Should().Be(UpdatesReasons.NoAddress);

            Mocker.Resolve<IAvailabilitySettingsStore>().Set("http://requestarr:9797", 10);
            Availability.UpdatesFor(Instance("Radarr")).Reason.Should().Be(UpdatesReasons.NotChecked);

            Radarr.WebhookUnreachable = true;
            await Availability.SetUpWebhooksAsync();
            Availability.UpdatesFor(Instance("Radarr")).Should().Be(new InstanceUpdates(false, UpdatesReasons.Unreachable, 10));
            Availability.UpdatesFor(Instance("Sonarr")).Reason.Should().Be(UpdatesReasons.RightAway);

            var reached = await Availability.TestAddressAsync("http://192.168.1.10:9797");

            reached.Where(r => r.Problem == null).Select(r => r.Name).Should().Equal("Sonarr");
            reached.Where(r => r.Problem != null).Select(r => r.Name).Should().BeEquivalentTo("Radarr", "Radarr 4K");
            reached.Single(r => r.Name == "Radarr").Problem.Should().Contain("192.168.1.10:9797");
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public async Task an_instance_that_cannot_reach_requestarr_is_a_health_warning_and_polling_covers_it()
        {
            Mocker.Resolve<IAvailabilitySettingsStore>().Set("http://requestarr:9797", 10);
            Radarr.WebhookUnreachable = true;

            await Availability.SetUpWebhooksAsync();

            Availability.WebhookProblems.Keys.Should().BeEquivalentTo("Radarr", "Radarr 4K");
            Availability.WebhookProblems["Radarr"].Should().Contain("http://requestarr:9797").And.Contain("every 10 minutes").And.NotContain(Token(Instance("Radarr").Id));
            var health = new WebhookHealthCheck(Availability, Mocker.Resolve<NzbDrone.Core.Localization.ILocalizationService>()).Check();
            health.Type.Should().Be(HealthCheckResult.Warning);
            health.Reason.Should().Be(HealthCheckReason.WebhookUnreachable);

            Radarr.WebhookUnreachable = false;
            await Availability.SetUpWebhooksAsync();
            Availability.WebhookProblems.Should().BeEmpty();
            ExceptionVerification.ExpectedWarns(2);
        }
    }
}
