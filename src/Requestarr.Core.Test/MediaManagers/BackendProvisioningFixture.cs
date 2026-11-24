using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.MediaManagers
{
    // Sonarr and Radarr instances come from the provisioning file: several per type, one default per type, and the
    // access new members get. An instance left out of the file is turned off, never deleted.
    [TestFixture]
    public class BackendProvisioningFixture : RequestScenario
    {
        private IBackendService Backends => Mocker.Resolve<IBackendService>();

        [Test]
        public void provisioning_should_create_several_instances_per_type()
        {
            Backends.All().Select(b => (b.Implementation, b.Name)).Should().BeEquivalentTo(new[] { ("Radarr", "Radarr"), ("Radarr", "Radarr 4K"), ("Sonarr", "Sonarr") });
            Instance("Sonarr").Config.SeriesType.Should().Be("anime");
            Instance("Radarr").Config.QualityProfile.Should().Be("HD-1080p");
        }

        [Test]
        public void instances_dropped_from_provisioning_should_be_disabled_not_deleted()
        {
            Backends.SyncFromProvisioning(new BackendsProvisioning { Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr", Url = "http://r", ApiKey = "k" } } });

            Instance("Radarr 4K").Enable.Should().BeFalse();
            Instance("Sonarr").Enable.Should().BeFalse();
            Instance("Radarr").Enable.Should().BeTrue();
        }

        [Test]
        public void provisioning_sets_the_default_access_for_new_members_and_auto_approve_when_left_out()
        {
            Backends.SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr", Url = "http://r", ApiKey = "k", DefaultAccess = "approval" } },
                Sonarr = new List<BackendInstanceProvisioning> { new() { Name = "Sonarr", Url = "http://s", ApiKey = "k" } }
            });

            Backends.DefaultAccess(ContentType.Movie).Should().Be(AccessState.NeedsApproval);
            Backends.DefaultAccess(ContentType.Tv).Should().Be(AccessState.AutoApprove);
            Backends.DefaultAccess(ContentType.Music).Should().Be(AccessState.Off, "no instance for music");
        }

        [Test]
        public void default_instance_is_the_marked_one_else_the_first()
        {
            Backends.Default(ContentType.Movie).Name.Should().Be("Radarr");
            Backends.Default(ContentType.Tv).Name.Should().Be("Sonarr");
        }

        [Test]
        public void provisioning_should_reject_two_defaults_for_one_type()
        {
            var loader = Mocker.Resolve<ProvisioningLoader>();
            loader.EnvironmentLookup = _ => null;

            Action act = () => loader.Parse("backends:\n  radarr:\n    - name: A\n      url: http://a\n      apiKey: k\n      default: true\n    - name: B\n      url: http://b\n      apiKey: k\n      default: true");

            act.Should().Throw<ProvisioningException>().WithMessage("*only one instance can be the default*");
        }
    }
}
