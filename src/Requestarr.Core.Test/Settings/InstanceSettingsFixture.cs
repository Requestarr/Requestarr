using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Converters;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Settings
{
    [TestFixture]
    public class InstanceSettingsFixture : CoreTest<InstanceSettingsService>
    {
        private static readonly Actor Admin = Actor.Web("owner");

        private List<BackendInstance> _instances;

        [SetUp]
        public void Setup()
        {
            _instances = new List<BackendInstance>
            {
                new() { Id = 1, Name = "Sonarr", Implementation = BackendImplementations.Sonarr, Settings = new BackendSettings { Url = "http://sonarr:8989", ApiKey = "old", Purpose = InstancePurpose.Everything } }
            };

            Mocker.GetMock<IBackendService>().Setup(b => b.All()).Returns(() => _instances);
            Mocker.GetMock<IBackendService>().Setup(b => b.Find(It.IsAny<int>())).Returns((int id) => _instances.SingleOrDefault(i => i.Id == id));
            Mocker.GetMock<IBackendService>().Setup(b => b.FindByName(It.IsAny<string>())).Returns((string name) => _instances.SingleOrDefault(i => i.Name == name));
            Mocker.GetMock<IBackendService>().SetupGet(b => b.LastResults).Returns(new List<BackendCheckResult>());
            Mocker.GetMock<IBackendRepository>().Setup(r => r.Upsert(It.IsAny<BackendInstance>())).Returns((BackendInstance i) =>
            {
                if (i.Id == 0)
                {
                    i.Id = _instances.Max(e => e.Id) + 1;
                    _instances.Add(i);
                }

                return i;
            });
        }

        private static InstanceInput Input(string name, string url, string apiKey = null, bool isDefault = false, string implementation = BackendImplementations.Sonarr) =>
            new(implementation, name, url, apiKey, "HD-1080p", "/tv", "standard", isDefault ? InstancePurpose.Everything : InstancePurpose.Nothing, true, false, true, "dub", null, null, null, null);

        [Test]
        public async Task should_add_an_instance_record_it_and_move_everything_to_it_when_chosen()
        {
            var result = await Subject.SaveAsync(null, Input("Sonarr 4K", "http://sonarr4k:8989", "key", isDefault: true), Admin);

            result.Problem.Should().BeNull();
            result.Instance.ApiKeySet.Should().BeTrue();
            result.Instance.Settings.ApiKey.Should().BeNull();
            _instances[0].Config.Purpose.Should().Be(InstancePurpose.Nothing, "each purpose belongs to one instance per type");
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.ChangeAction, "instance", "1", "Sonarr: for all other shows moved to Sonarr 4K"), Times.Once());
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.AddAction, "instance", "2", "Sonarr 4K: Sonarr at http://sonarr4k:8989"), Times.Once());
            Mocker.GetMock<IBackendService>().Verify(b => b.CheckAllAsync(), Times.Once());
        }

        [Test]
        public async Task turning_off_the_instance_for_all_other_shows_hands_it_to_nobody_and_anime_is_sonarrs_only()
        {
            await Subject.SaveAsync(null, Input("Sonarr Anime", "http://anime:8989", "key") with { Purpose = InstancePurpose.Anime }, Admin);

            var turnedOff = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { Enabled = false }, Admin);
            turnedOff.Problem.Should().BeNull("regular shows are paused until an admin sets another to take them");
            _instances.Select(i => (i.Enable, i.Config.Purpose)).Should().Equal((false, InstancePurpose.Everything), (true, InstancePurpose.Anime));

            var givenAnotherPurpose = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989"), Admin);
            givenAnotherPurpose.Problem.Should().BeNull();
            _instances[1].Config.Purpose.Should().Be(InstancePurpose.Anime);

            var radarr = await Subject.SaveAsync(null, Input("Radarr Anime", "http://radarr:7878", "key", implementation: BackendImplementations.Radarr) with { Purpose = InstancePurpose.Anime }, Admin);
            radarr.Problem.Should().Be("Anime is for Sonarr only.");
        }

        [Test]
        public async Task an_instance_alone_in_its_type_is_for_everything()
        {
            var result = await Subject.SaveAsync(null, Input("Radarr", "http://radarr:7878", "key", implementation: BackendImplementations.Radarr), Admin);

            result.Instance.Settings.Purpose.Should().Be(InstancePurpose.Everything);
        }

        [Test]
        public async Task should_record_what_changed_and_never_the_api_key_itself()
        {
            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:9999", "new", isDefault: true), Admin);

            _instances[0].Config.ApiKey.Should().Be("new");
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.ChangeAction, "instance", "1", It.Is<string>(d => IsChangeRecord(d))), Times.Once());
        }

        private static bool IsChangeRecord(string detail)
        {
            return detail.Contains("address from http://sonarr:8989 to http://sonarr:9999") && detail.Contains("API key replaced") && !detail.Contains("new");
        }

        [Test]
        public async Task should_keep_and_record_the_address_for_links_and_refuse_one_that_isnt_a_web_address()
        {
            var bad = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { BrowserUrl = "nas.local:8989" }, Admin);
            bad.Problem.Should().Contain("address for links");

            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { BrowserUrl = " http://nas.local:8989 " }, Admin);

            _instances[0].Config.BrowserUrl.Should().Be("http://nas.local:8989");
            Subject.All().Single(i => i.Id == 1).Settings.BrowserUrl.Should().Be("http://nas.local:8989");
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.ChangeAction, "instance", "1", It.Is<string>(d => d.Contains("address for links from none to http://nas.local:8989"))), Times.Once());
        }

        [Test]
        public async Task should_keep_the_saved_api_key_when_none_is_given()
        {
            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true), Admin);

            _instances[0].Config.ApiKey.Should().Be("old");
        }

        [Test]
        public async Task should_send_the_saved_api_key_only_to_the_saved_address()
        {
            Mocker.GetMock<IBackendService>().Setup(b => b.ProbeAsync(It.IsAny<string>(), It.IsAny<BackendSettings>())).ReturnsAsync(BackendProbe.Failed("unreachable"));

            await Subject.TestAsync(1, Input("Sonarr", "http://sonarr:8989"));
            await Subject.TestAsync(1, Input("Sonarr", "http://elsewhere:8989"));

            Mocker.GetMock<IBackendService>().Verify(b => b.ProbeAsync(BackendImplementations.Sonarr, It.Is<BackendSettings>(s => s.Url == "http://sonarr:8989" && s.ApiKey == "old")), Times.Once());
            Mocker.GetMock<IBackendService>().Verify(b => b.ProbeAsync(BackendImplementations.Sonarr, It.Is<BackendSettings>(s => s.Url == "http://elsewhere:8989")), Times.Never());
            (await Subject.TestAsync(1, Input("Sonarr", "http://elsewhere:8989"))).Problem.Should().Be(ConnectionProblems.NoApiKey("Sonarr"), "a new address needs its key typed again");
        }

        [TestCase("http://localhost:8989", "inside Requestarr's container, localhost means Requestarr itself")]
        [TestCase("http://127.0.0.1:8989", "inside Requestarr's container, 127.0.0.1 means Requestarr itself")]
        [TestCase("http://nas-name:8989", "Couldn't find a server named nas-name. Inside Requestarr's container, a computer's network name may not work; use its IP address instead.")]
        public async Task should_say_in_plain_words_why_it_cant_reach_an_instance(string url, string expected)
        {
            var notFound = new System.Net.Http.HttpRequestException("No such host is known.", new System.Net.Sockets.SocketException((int)System.Net.Sockets.SocketError.HostNotFound));
            Mocker.GetMock<IBackendService>().Setup(b => b.ProbeAsync(It.IsAny<string>(), It.IsAny<BackendSettings>())).ThrowsAsync(new BackendException($"{url}: No such host is known.", notFound));

            var result = await Subject.TestAsync(null, Input("Sonarr", url, "key"));

            result.Problem.Should().Contain(expected);
        }

        [TestCase("Sonarr", "http://sonarr2:8989", "key")]
        [TestCase("Sonarr 2", "sonarr2:8989", "key")]
        [TestCase("Sonarr 2", "http://sonarr2:8989", null)]
        public async Task should_refuse_an_instance_it_cant_use(string name, string url, string apiKey)
        {
            var result = await Subject.SaveAsync(null, Input(name, url, apiKey), Admin);

            result.Problem.Should().NotBeNullOrEmpty();
            _instances.Should().HaveCount(1);
        }

        [Test]
        public async Task should_keep_show_and_record_the_default_access_for_new_members()
        {
            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { DefaultAccess = AccessState.AutoApprove }, Admin);

            _instances[0].Config.DefaultAccess.Should().Be(AccessState.AutoApprove);
            Subject.All().Single(i => i.Id == 1).Settings.DefaultAccess.Should().Be(AccessState.AutoApprove);
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.ChangeAction, "instance", "1", It.Is<string>(d => d.Contains("default access for new members from needs approval to auto-approve"))), Times.Once());
        }

        [Test]
        public async Task should_refuse_a_default_access_that_isnt_one_of_the_three()
        {
            var result = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { DefaultAccess = (AccessState)7 }, Admin);

            result.Problem.Should().Contain("for new members");
            _instances[0].Config.DefaultAccess.Should().Be(AccessState.NeedsApproval);
        }

        // As stored before the setting existed, and as sent by anything that leaves it out: what new members got then.
        [Test]
        public void should_read_an_instance_saved_before_the_setting_as_needs_approval()
        {
            var stored = new EmbeddedDocumentConverter<BackendSettings>().Parse("{\"url\": \"http://sonarr:8989\", \"isDefault\": true}");

            var sent = JsonSerializer.Deserialize<InstanceInput>("{\"implementation\": \"Sonarr\", \"name\": \"Sonarr\", \"url\": \"http://sonarr:8989\"}", new JsonSerializerOptions(JsonSerializerDefaults.Web));

            stored.DefaultAccess.Should().Be(AccessState.NeedsApproval);
            sent.DefaultAccess.Should().Be(AccessState.NeedsApproval);
        }

        [Test]
        public async Task should_refuse_changes_where_the_provisioning_file_sets_the_instances()
        {
            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);

            var result = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:9999"), Admin);

            result.Problem.Should().Be(AppSettingsService.ProvisionedProblem);
            _instances[0].Config.Url.Should().Be("http://sonarr:8989");
        }
    }
}
