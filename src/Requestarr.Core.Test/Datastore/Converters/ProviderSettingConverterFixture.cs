using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Converters;
using NzbDrone.Core.ThingiProvider;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore.Converters
{
    [Ignore("To reinstate once dapper changes worked out")]
    [TestFixture]
    public class ProviderSettingConverterFixture : CoreTest<ProviderSettingConverter>
    {
        [Test]
        public void should_return_null_config_if_config_is_null()
        {
            Subject.Parse(null).Should().Be(NullConfig.Instance);
        }

        [TestCase(null)]
        [TestCase("")]
        public void should_return_null_config_if_config_is_empty(object dbValue)
        {
            Subject.Parse(dbValue).Should().Be(NullConfig.Instance);
        }
    }
}
