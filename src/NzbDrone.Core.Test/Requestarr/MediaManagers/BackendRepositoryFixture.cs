using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.MediaManagers
{
    [TestFixture]
    public class BackendRepositoryFixture : DbTest<BackendRepository, BackendInstance>
    {
        private const string SavedBefore =
            "INSERT INTO \"Backends\" (\"Name\", \"Implementation\", \"Settings\", \"ConfigContract\", \"Enable\", \"Tags\", \"Added\", \"DownAnnounceOffered\") " +
            "VALUES ('Sonarr', 'Sonarr', '{\"url\":\"http://sonarr:8989\",\"apiKey\":\"key\",\"isDefault\":true,\"defaultAccess\":\"autoApprove\",\"dubRequired\":[\"dub\"]}', " +
            "'BackendSettings', 1, '[]', '2026-11-01T00:00:00Z', 0)";

        // An instance as Requestarr stored it before instances were providers reads back unchanged.
        [Test]
        public void should_read_an_instance_saved_before_instances_were_providers()
        {
            using (var connection = Mocker.Resolve<IMainDatabase>().OpenConnection())
            {
                connection.Execute(SavedBefore);
            }

            var instance = Subject.All().Single();

            instance.Enable.Should().BeTrue();
            instance.Config.Url.Should().Be("http://sonarr:8989");
            instance.Config.Purpose.Should().Be(InstancePurpose.Everything, "default: true still reads as Everything");
            instance.Config.DefaultAccess.Should().Be(AccessState.AutoApprove);
            instance.Config.DubRequired.Should().Equal("dub");
            instance.Config.AnimeEnabled.Should().BeTrue();
        }

        [Test]
        public void should_store_its_settings_as_before()
        {
            Subject.Insert(new BackendInstance
            {
                Name = "Radarr",
                Implementation = BackendImplementations.Radarr,
                Settings = new BackendSettings { Url = "http://radarr:7878", DefaultAccess = AccessState.Off }
            });

            using var connection = Mocker.Resolve<IMainDatabase>().OpenConnection();
            var row = connection.QuerySingle<(string Settings, string ConfigContract)>("SELECT \"Settings\", \"ConfigContract\" FROM \"Backends\"");

            row.ConfigContract.Should().Be("BackendSettings");
            row.Settings.Should().Contain("\"url\": \"http://radarr:7878\"").And.Contain("\"defaultAccess\": \"off\"").And.NotContain("defaultAccessChoice");
        }
    }
}
