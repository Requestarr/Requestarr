using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // A request goes to the Sonarr or Radarr whose purpose matches what is asked for, never by member: anime to the
    // Anime Sonarr when one is turned on, everything else (and anime without one) to the type's Everything instance.
    [TestFixture]
    public class RoutingByContentFixture : CoreTest<BackendService>
    {
        private BackendInstance _everything;
        private BackendInstance _anime;
        private BackendInstance _radarr;

        [SetUp]
        public void SetUp()
        {
            _everything = Instance(1, "Sonarr", BackendImplementations.Sonarr, InstancePurpose.Everything);
            _anime = Instance(2, "Sonarr Anime", BackendImplementations.Sonarr, InstancePurpose.Anime);
            _radarr = Instance(3, "Radarr", BackendImplementations.Radarr, InstancePurpose.Everything);
            Mocker.GetMock<IBackendRepository>().Setup(r => r.All()).Returns(new List<BackendInstance> { _anime, _everything, _radarr });
        }

        private static BackendInstance Instance(int id, string name, string implementation, InstancePurpose purpose) =>
            new() { Id = id, Name = name, Implementation = implementation, Enable = true, Settings = new BackendSettings { Purpose = purpose } };

        [Test]
        public void anime_goes_to_the_anime_sonarr_and_everything_else_to_everything()
        {
            Subject.ForRequest(ContentType.Tv, isAnime: true).Should().BeSameAs(_anime);
            Subject.ForRequest(ContentType.Tv, isAnime: false).Should().BeSameAs(_everything);
            Subject.ForRequest(ContentType.Movie, isAnime: true).Should().BeSameAs(_radarr, "anime films go to Radarr's Everything");
        }

        [Test]
        public void with_the_anime_sonarr_turned_off_anime_goes_to_everything()
        {
            _anime.Enable = false;

            Subject.ForRequest(ContentType.Tv, isAnime: true).Should().BeSameAs(_everything);
        }

        [Test]
        public void provisioning_keeps_one_instance_per_purpose_and_gives_everything_to_the_first_spare()
        {
            var saved = new List<BackendInstance>();
            Mocker.GetMock<IBackendRepository>().Setup(r => r.All()).Returns(new List<BackendInstance>());
            Mocker.GetMock<IBackendRepository>().Setup(r => r.Upsert(It.IsAny<BackendInstance>())).Returns<BackendInstance>(b =>
            {
                saved.Add(b);
                return b;
            });

            Subject.SyncFromProvisioning(new BackendsProvisioning
            {
                Sonarr = new List<BackendInstanceProvisioning> { new() { Name = "A", Purpose = "anime" }, new() { Name = "B", Purpose = "anime" }, new() { Name = "C" } },
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "R", Purpose = "anime" } }
            });

            saved.ToDictionary(b => b.Name, b => b.Config.Purpose).Should().Equal(new Dictionary<string, InstancePurpose>
            {
                ["A"] = InstancePurpose.Anime,
                ["B"] = InstancePurpose.Everything,
                ["C"] = InstancePurpose.Nothing,
                ["R"] = InstancePurpose.Everything
            });
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void the_types_main_instance_is_its_everything_one_even_when_another_was_added_first()
        {
            Subject.Default(ContentType.Tv).Should().BeSameAs(_everything, "Sonarr Anime is listed first but is for anime");
        }
    }
}
