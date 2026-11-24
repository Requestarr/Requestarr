using FluentAssertions;
using NzbDrone.Common.Extensions;
using NzbDrone.Core.HealthCheck;

namespace Requestarr.Core.Test.HealthChecks.Checks
{
    public static class HealthCheckFixtureExtensions
    {
        public static void ShouldBeOk(this NzbDrone.Core.HealthCheck.HealthCheck result)
        {
            result.Type.Should().Be(HealthCheckResult.Ok);
        }

        public static void ShouldBeNotice(this NzbDrone.Core.HealthCheck.HealthCheck result, string message = null)
        {
            result.Type.Should().Be(HealthCheckResult.Notice);

            if (message.IsNotNullOrWhiteSpace())
            {
                result.Message.Should().Contain(message);
            }
        }

        public static void ShouldBeWarning(this NzbDrone.Core.HealthCheck.HealthCheck result, string message = null, string wikiFragment = null)
        {
            result.Type.Should().Be(HealthCheckResult.Warning);

            if (message.IsNotNullOrWhiteSpace())
            {
                result.Message.Should().Contain(message);
            }

            if (wikiFragment.IsNotNullOrWhiteSpace())
            {
                result.WikiUrl.ToString().Should().Contain(wikiFragment);
            }
        }

        public static void ShouldBeError(this NzbDrone.Core.HealthCheck.HealthCheck result, string message = null, string wikiFragment = null)
        {
            result.Type.Should().Be(HealthCheckResult.Error);

            if (message.IsNotNullOrWhiteSpace())
            {
                result.Message.Should().Contain(message);
            }

            if (wikiFragment.IsNotNullOrWhiteSpace())
            {
                result.WikiUrl.ToString().Should().Contain(wikiFragment);
            }
        }
    }
}
