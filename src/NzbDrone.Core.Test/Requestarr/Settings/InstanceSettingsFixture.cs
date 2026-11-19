using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Settings;

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
                new() { Id = 1, Name = "Sonarr", Implementation = BackendImplementations.Sonarr, Settings = new BackendSettings { Url = "http://sonarr:8989", ApiKey = "old", IsDefault = true } }
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
            new(implementation, name, url, apiKey, "HD-1080p", "/tv", "standard", isDefault, true, false, true, "dub", null, null, null, null);

        [Test]
        public async Task should_add_an_instance_record_it_and_make_it_the_only_default()
        {
            var result = await Subject.SaveAsync(null, Input("Sonarr 4K", "http://sonarr4k:8989", "key", isDefault: true), Admin);

            result.Problem.Should().BeNull();
            result.Instance.ApiKeySet.Should().BeTrue();
            result.Instance.Settings.ApiKey.Should().BeNull();
            _instances[0].Settings.IsDefault.Should().BeFalse();
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.AddAction, "instance", "2", "Sonarr 4K: Sonarr at http://sonarr4k:8989"), Times.Once());
            Mocker.GetMock<IBackendService>().Verify(b => b.CheckAllAsync(), Times.Once());
        }

        [Test]
        public async Task should_record_what_changed_and_never_the_api_key_itself()
        {
            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:9999", "new", isDefault: true), Admin);

            _instances[0].Settings.ApiKey.Should().Be("new");
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.ChangeAction, "instance", "1", It.Is<string>(d => IsChangeRecord(d))), Times.Once());
        }

        private static bool IsChangeRecord(string detail)
        {
            return detail.Contains("address from http://sonarr:8989 to http://sonarr:9999") && detail.Contains("API key replaced") && !detail.Contains("new");
        }

        [Test]
        public async Task should_keep_and_record_the_address_in_your_browser_and_refuse_one_that_isnt_a_web_address()
        {
            var bad = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { BrowserUrl = "nas.local:8989" }, Admin);
            bad.Problem.Should().Contain("address in your browser");

            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true) with { BrowserUrl = " http://nas.local:8989 " }, Admin);

            _instances[0].Settings.BrowserUrl.Should().Be("http://nas.local:8989");
            Subject.All().Single(i => i.Id == 1).Settings.BrowserUrl.Should().Be("http://nas.local:8989");
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceSettingsService.ChangeAction, "instance", "1", It.Is<string>(d => d.Contains("address in your browser from none to http://nas.local:8989"))), Times.Once());
        }

        [Test]
        public async Task should_keep_the_saved_api_key_when_none_is_given()
        {
            await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:8989", isDefault: true), Admin);

            _instances[0].Settings.ApiKey.Should().Be("old");
        }

        [Test]
        public async Task should_send_the_saved_api_key_only_to_the_saved_address()
        {
            Mocker.GetMock<IBackendService>().Setup(b => b.ProbeAsync(It.IsAny<string>(), It.IsAny<BackendSettings>())).ReturnsAsync(BackendProbe.Failed("unreachable"));

            await Subject.TestAsync(1, Input("Sonarr", "http://sonarr:8989"));
            await Subject.TestAsync(1, Input("Sonarr", "http://elsewhere:8989"));

            Mocker.GetMock<IBackendService>().Verify(b => b.ProbeAsync(BackendImplementations.Sonarr, It.Is<BackendSettings>(s => s.Url == "http://sonarr:8989" && s.ApiKey == "old")), Times.Once());
            Mocker.GetMock<IBackendService>().Verify(b => b.ProbeAsync(BackendImplementations.Sonarr, It.Is<BackendSettings>(s => s.Url == "http://elsewhere:8989" && s.ApiKey == null)), Times.Once());
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
        public async Task should_refuse_changes_where_the_provisioning_file_sets_the_instances()
        {
            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);

            var result = await Subject.SaveAsync(1, Input("Sonarr", "http://sonarr:9999"), Admin);

            result.Problem.Should().Be(AppSettingsService.ProvisionedProblem);
            _instances[0].Settings.Url.Should().Be("http://sonarr:8989");
        }
    }
}
