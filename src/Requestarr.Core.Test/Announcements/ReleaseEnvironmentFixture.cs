using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Core.Test.Announcements
{
    // The environment a server runs in (dev, QA, beta for staging, production for anything else) decides the web app's
    // icon set, how the version posts are kept, which versions staging has and how a build's version is shown.
    [TestFixture]
    public class ReleaseEnvironmentFixture
    {
        private static ReleaseEnvironment WithVariable(string name, string value) =>
            new(Mock.Of<IConfigService>()) { Variable = v => v == name ? value : null };

        [TestCase("dev", "dev", VersionPostMode.CurrentOnly)]
        [TestCase("qa", "qa", VersionPostMode.NotYetStaged)]
        [TestCase(" QA ", "qa", VersionPostMode.NotYetStaged)]
        [TestCase("staging", "beta", VersionPostMode.Standing)]
        [TestCase(null, "production", VersionPostMode.Standing)]
        [TestCase("", "production", VersionPostMode.Standing)]
        [TestCase("anything else", "production", VersionPostMode.Standing)]
        public void the_environment_decides_the_icon_set_and_how_version_posts_are_kept(string environment, string set, VersionPostMode posts)
        {
            var release = WithVariable(ReleaseEnvironment.EnvironmentVariable, environment);

            ReleaseEnvironment.IconSetFor(environment).Should().Be(set);
            release.IconSet.Should().Be(set);
            release.VersionPosts.Should().Be(posts);
        }

        // A published image runs on beta and production alike, so without the stack's variable the role chosen in the
        // first-run setup decides; the stack's variable, on dev and QA, always wins.
        [TestCase(null, "beta", "beta")]
        [TestCase(null, "production", "production")]
        [TestCase(null, "", "production")]
        [TestCase("dev", "beta", "dev")]
        [TestCase("qa", "production", "qa")]
        public void without_the_stacks_variable_the_servers_role_decides(string environment, string role, string set)
        {
            var config = Mock.Of<IConfigService>(c => c.ServerRole == role);
            var release = new ReleaseEnvironment(config) { Variable = v => v == ReleaseEnvironment.EnvironmentVariable ? environment : null };

            release.IconSet.Should().Be(set);
            release.SetByStack.Should().Be(environment != null);
            release.VersionPosts.Should().Be(PostsFor(environment));
        }

        private static VersionPostMode PostsFor(string environment) => environment switch { "dev" => VersionPostMode.CurrentOnly, "qa" => VersionPostMode.NotYetStaged, _ => VersionPostMode.Standing };

        [Test]
        public void the_versions_staging_has_are_read_from_the_stacks_variable()
        {
            WithVariable(ReleaseEnvironment.StagedVersionsVariable, "0.1.0, 0.2.0,junk").Staged.Should().BeEquivalentTo(new[] { new Version(0, 1, 0), new Version(0, 2, 0) });
            WithVariable(ReleaseEnvironment.StagedVersionsVariable, null).Staged.Should().BeEmpty();
        }

        [Test]
        public void a_version_shows_as_the_build_label_only_for_the_version_the_build_runs()
        {
            string Shown(string label, string version) => WithVariable(ReleaseEnvironment.ReleaseVersionVariable, label).Shown(new Version(version));

            Shown("0.7.0004-dev.4f2a9c1", "0.7.4.11354").Should().Be("0.7.0004-dev.4f2a9c1");
            Shown("0.7.0004-dev", "0.7.4").Should().Be("0.7.0004-dev", "a goal's numbered dev build shows its number");
            Shown("0.7.0004-qa", "0.7.4").Should().Be("0.7.0004-qa", "and QA the same number");
            Shown(" 0.7.0004-qa.4f2a9c1 ", "0.7.4").Should().Be("0.7.0004-qa.4f2a9c1");
            Shown("0.7.0004", "0.7.4").Should().Be("0.7.0004", "a build sent to testers shows its number");
            Shown("0.7.0004-dev.4f2a9c1", "0.6.0").Should().Be("0.6.0", "an older version keeps its own number");
            Shown(null, "0.7.12").Should().Be("0.7.0012", "a build without a label shows its number, four digits from 0.7 on");
            Shown("0.7.0004-beta.1", "0.7.4").Should().Be("0.7.0004", "only the labels Requestarr's builds use count");
            Shown("0.7.4-dev.4f2a9c1", "0.7.4").Should().Be("0.7.0004", "a label without the four digits isn't one");
        }
    }
}
