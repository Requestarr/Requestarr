using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Backends.MediaManagers;
using Requestarr.Core.Settings;

namespace Requestarr.Core.Test.MediaManagers
{
    [TestFixture]
    public class MediaManagerFixture : CoreTest<MediaManagerFactory>
    {
        private List<BackendInstance> _stored;

        [SetUp]
        public void Setup()
        {
            _stored = new List<BackendInstance>
            {
                new() { Id = 1, Name = "Sonarr", Implementation = BackendImplementations.Sonarr, Settings = new BackendSettings { Url = "http://sonarr:8989", ApiKey = "key" } },
                new() { Id = 2, Name = "Lidarr", Implementation = BackendImplementations.Lidarr, Settings = new BackendSettings { Url = "http://lidarr:8686" } }
            };

            var instances = Mocker.GetMock<IInstanceSettingsService>().Object;
            var sonarr = new Sonarr(instances);
            var radarr = new Radarr(instances);

            Mocker.SetConstant<IEnumerable<IMediaManager>>(new IMediaManager[] { sonarr, radarr });
            Mocker.GetMock<IServiceProvider>().Setup(c => c.GetService(typeof(Sonarr))).Returns(sonarr);
            Mocker.GetMock<IServiceProvider>().Setup(c => c.GetService(typeof(Radarr))).Returns(radarr);
            Mocker.GetMock<IBackendRepository>().Setup(r => r.All()).Returns(() => _stored);
            Mocker.GetMock<IBackendRepository>().Setup(r => r.Get(It.IsAny<int>())).Returns((int id) => _stored.Single(i => i.Id == id));
        }

        [TearDown]
        public void Forget()
        {
            ActingAdmin.Current = null;
        }

        private static BackendInstance Form(int id = 0) => new()
        {
            Id = id,
            Name = "Sonarr 4K",
            Implementation = BackendImplementations.Sonarr,
            Settings = new BackendSettings { Url = "http://sonarr4k:8989", ApiKey = "new", DubRequired = new List<string> { "dub", "dual audio" } }
        };

        private static IDictionary<string, string> NoQuery => new Dictionary<string, string>();

        private void SaveAnswers(string problem)
        {
            var result = problem == null ? new InstanceSaveResult(new InstanceView(1, null, true, true, null), null) : new InstanceSaveResult(null, problem);

            Mocker.GetMock<IInstanceSettingsService>()
                  .Setup(s => s.SaveAsync(It.IsAny<int?>(), It.IsAny<InstanceInput>(), It.IsAny<Actor>()))
                  .ReturnsAsync(result);
        }

        private void TestAnswers(int? id, string problem, params string[] profiles)
        {
            Mocker.GetMock<IInstanceSettingsService>()
                  .Setup(s => s.TestAsync(id, It.IsAny<InstanceInput>()))
                  .ReturnsAsync(new InstanceTestResult(problem, profiles, Array.Empty<string>()));
        }

        [Test]
        public void should_list_only_instances_a_media_manager_handles()
        {
            Subject.All().Select(i => i.Name).Should().Equal("Sonarr");
        }

        [Test]
        public void should_add_through_the_settings_rules_as_the_admin_acting()
        {
            SaveAnswers(null);
            ActingAdmin.Current = Actor.Web("owner");

            var saved = Subject.Create(Form());

            saved.Id.Should().Be(1);
            Mocker.GetMock<IInstanceSettingsService>().Verify(s => s.SaveAsync(
                null,
                It.Is<InstanceInput>(i => i.Name == "Sonarr 4K" && i.ApiKey == "new" && i.DubRequired == "dub, dual audio"),
                Actor.Web("owner")));
        }

        [Test]
        public void should_refuse_a_change_with_the_settings_rules_reason()
        {
            SaveAnswers("Give the instance a name no other instance has.");

            Action change = () => Subject.Update(Form(1));

            change.Should().Throw<ValidationException>().Which.Errors.Single().ErrorMessage.Should().Be("Give the instance a name no other instance has.");
        }

        [Test]
        public void should_offer_the_quality_profiles_and_series_types_its_settings_choose_from()
        {
            TestAnswers(null, null, "HD-1080p", "Ultra-HD");

            var profiles = Subject.RequestAction(Form(), MediaManagerActions.QualityProfiles, NoQuery);
            var types = Subject.RequestAction(Form(), MediaManagerActions.SeriesTypes, NoQuery);

            JsonSerializer.Serialize(profiles).Should().Be("{\"options\":[{\"value\":\"HD-1080p\",\"name\":\"HD-1080p\"},{\"value\":\"Ultra-HD\",\"name\":\"Ultra-HD\"}]}");
            JsonSerializer.Serialize(types).Should().Contain("{\"value\":\"anime\",\"name\":\"Anime\"}");
        }

        [Test]
        public void should_fail_its_test_on_the_address_with_the_reason_it_could_not_connect()
        {
            TestAnswers(1, "Couldn't reach Sonarr.");

            var failure = Subject.Test(Form(1)).Errors.Single();

            failure.PropertyName.Should().Be(nameof(BackendSettings.Url));
            failure.ErrorMessage.Should().Be("Couldn't reach Sonarr.");
        }
    }
}
