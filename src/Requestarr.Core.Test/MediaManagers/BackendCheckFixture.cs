using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using Requestarr.Core.Backends;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.MediaManagers
{
    // Each Sonarr and Radarr instance is checked: a supported version of the right app, reachable, with the quality
    // profile and root folder it is set to use. A failing instance raises the health warning.
    [TestFixture]
    public class BackendCheckFixture : RequestScenario
    {
        private IBackendService Backends => Mocker.Resolve<IBackendService>();

        [Test]
        public async Task check_should_pass_for_supported_versions_and_resolve_profile()
        {
            await Backends.CheckAllAsync();

            Backends.LastResults.Should().OnlyContain(r => r.Ok);
            Backends.LastResults.Single(r => r.Name == "Radarr").QualityProfileId.Should().Be(4);
            Mocker.Resolve<BackendHealthCheck>().Check().Type.Should().Be(HealthCheckResult.Ok);
        }

        [TestCase("Sonarr", "3.0.10", "Sonarr 3.0.10 is not supported; version 4 or later is required")]
        [TestCase("Radarr", "4.7.5", "Radarr 4.7.5 is not supported; version 5 or later is required")]
        public async Task check_should_reject_old_versions(string name, string version, string message)
        {
            (name == "Sonarr" ? (FakeArr)Sonarr : Radarr).Version = Version.Parse(version);

            await Backends.CheckAllAsync();

            Backends.LastResults.Single(r => r.Name == name).Error.Should().Be(message);
            var health = Mocker.Resolve<BackendHealthCheck>().Check();
            health.Type.Should().Be(HealthCheckResult.Error);
            health.Message.Should().Contain(message);

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task check_should_report_wrong_app_unreachable_profile_and_root_folder()
        {
            Sonarr.AppName = "Radarr";
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Sonarr").Error.Should().Contain("is Radarr, not Sonarr");

            Sonarr.AppName = "Sonarr";
            Sonarr.Unreachable = true;
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Sonarr").Error.Should().Contain("Connection refused");

            Sonarr.Unreachable = false;
            Sonarr.Profiles.Clear();
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Sonarr").Error.Should().Contain("quality profile 'HD-1080p' not found");

            Radarr.RootFolders.Clear();
            Radarr.RootFolders.Add("/elsewhere");
            await Backends.CheckAllAsync();
            Backends.LastResults.Single(r => r.Name == "Radarr").Error.Should().Contain("root folder '/data/media' not found");

            ExceptionVerification.IgnoreWarns();
        }
    }
}
