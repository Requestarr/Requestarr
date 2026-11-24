using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Imports;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Imports
{
    [TestFixture]
    public class HistorySettingsFixture : CoreTest<HistorySettings>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<IConfigService>().SetupAllProperties();
        }

        [Test]
        public void history_stays_out_of_new_channels_and_goes_in_the_feed_until_set_otherwise()
        {
            Mocker.GetMock<IConfigRepository>().Setup(r => r.All()).Returns(new List<Config>());
            Mocker.SetConstant<IConfigService>(Mocker.Resolve<ConfigService>());

            Subject.WhenOnboarding.Should().BeFalse();
            Subject.InFeed.Should().BeTrue();
        }

        [Test]
        public void a_provisioning_file_sets_both()
        {
            Subject.Apply(new ImportedHistoryProvisioning { WhenOnboarding = true, InFeed = false });

            Subject.WhenOnboarding.Should().BeTrue();
            Subject.InFeed.Should().BeFalse();
        }

        [Test]
        public void a_provisioning_file_without_them_leaves_both_as_they_are()
        {
            Subject.Apply(new ImportedHistoryProvisioning { WhenOnboarding = true });
            Subject.Apply(new ImportedHistoryProvisioning());
            Subject.Apply(null);

            Subject.WhenOnboarding.Should().BeTrue();
        }
    }
}
