using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.ThingiProvider;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.ThingiProvider
{
    [TestFixture]
    public class NullConfigFixture : CoreTest<NullConfig>
    {
        [Test]
        public void should_be_valid()
        {
            Subject.Validate().IsValid.Should().BeTrue();
        }
    }
}
