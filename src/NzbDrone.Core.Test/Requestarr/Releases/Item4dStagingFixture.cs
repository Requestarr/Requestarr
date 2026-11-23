using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Test.AnimeDub;

namespace Requestarr.Core.Test.Releases
{
    // Task 4d: the staging (beta) environment on the owner's server, published images, the staging promotion, and the
    // changelog rule. What runs is shown live in the task's reply; these keep the files honest.
    [TestFixture]
    public class Item4dStagingFixture
    {
        // Line endings as on Linux, whatever the checkout made them.
        private static string Read(params string[] path) => File.ReadAllText(Item4To6Fixture.RepoFile(path)).Replace("\r", string.Empty);

        [Test]
        public void the_beta_compose_file_runs_the_same_one_container()
        {
            var compose = Read("deploy", "beta", "compose.yaml");

            compose.Should().Contain("name: requestarr-beta").And.Contain("image: ghcr.io/requestarr/requestarr:beta").And.Contain("pull_policy: always");
            Regex.Matches(compose, @"^  (?<service>[\w-]+):\n    image:", RegexOptions.Multiline).Select(m => m.Groups["service"].Value).Should().Equal("requestarr");

            // The same four settings as Container Manager's Run screens, nothing else: the image needs no variable to run
            // as beta (the first-run setup's role says so), and its data is a folder on the server, not a volume.
            compose.Should().NotContain("REQUESTARR_ENVIRONMENT").And.NotContain("env_file").And.NotContain("${").And.NotContain("./").And.NotContain("PROVISIONING");
            compose.Should().Contain("x-values:").And.Contain("port: &port 19797").And.Contain("timezone: &timezone").And.Contain("published: *port").And.Contain("TZ: *timezone");
            compose.Should().Contain("type: bind").And.Contain("source: *folder").And.Contain("target: /config").And.NotContain("\nvolumes:");

            foreach (var retired in new[] { "update.sh", "check-folders.sh", "root-folders.sh", "env.example", "compose.yaml" })
            {
                File.Exists(Item4To6Fixture.RepoFile("deploy", "staging", retired)).Should().BeFalse($"{retired} is retired for beta");
            }

            Read(".dockerignore").Should().Contain("**/*.env").And.Contain("**/.env").And.Contain("docker/secrets.env");
        }

        [Test]
        public void images_are_published_for_two_architectures_on_staging_and_main_only()
        {
            var image = Read(".github", "workflows", "image.yml");

            image.Should().Contain("branches: [staging, main]").And.Contain("platforms: linux/amd64,linux/arm64").And.Contain("packages: write");
            image.Should().Contain("build_numbers.py image \"$EVENT\" \"$BRANCH\" \"$GITHUB_SHA\"", "the build-number script decides what is published");
            image.Should().Contain("BUILD_NUMBER=${{ steps.tags.outputs.number }}").And.Contain("RELEASE_VERSION=${{ steps.tags.outputs.label }}");
            image.Should().Contain("git push origin \"refs/tags/$RECORD\"", "each staging publish is recorded as the tag the next number is counted from");
            image.Should().Contain("docker buildx imagetools create").And.Contain("if: steps.tags.outputs.mode == 'build'", "ship it retags staging's image and never builds");
            Read("docker", "Dockerfile").Should().Contain($"ENV {ReleaseEnvironment.ReleaseVersionVariable}=$RELEASE_VERSION").And.Contain("-p:RequestarrVersion=\"$BUILD_NUMBER\"");
            Read(".github", "build_numbers.py").Should().Contain("test-[a-z0-9.-]+", "a hand-run publishes only a test tag");
            image.Should().Contain("org.opencontainers.image.source=").And.Contain("secret-looking environment variable").And.Contain("answers /ping");
            Read("docker", "Dockerfile").Should().Contain("FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk").And.Contain("ARG TARGETARCH");
        }

        [Test]
        public void staging_has_its_promotion_fix_back_and_drift()
        {
            var release = Read("release.sh");
            var build = Read(".github", "workflows", "build.yml");

            release.Should().Contain("promote-staging) shift; promote_staging").And.Contain("merge_into staging qa").And.Contain("merge_into qa dev");
            release.Should().Contain("ship-it) shift; ship_it").And.Contain("numbers release \"$work/release\" \"$number\"");
            release.Should().Contain("drift_pair \"$qa\" \"$staging\"").And.Contain("numbers check \"$kind\"");
            release.Should().Contain("numbers merge \"$work/ours\" \"$work/theirs\"").And.Contain("--dry-run) dry=1");
            release.Should().Contain("hold-staging) shift; hold_staging").And.Contain("staging is held by the owner").And.Contain("rm -f \"$hold\"", "the owner's hold blocks the gate until a promotion on their word lifts it");
            build.Should().Contain("python3 .github/build_numbers.py --self-test").And.Contain("pull_request:main) below=staging ;;");
            build.Should().Contain("pull_request:staging) below=qa ;;").And.Contain("HEAD_REF: ${{ github.event.pull_request.head.ref }}");
            Read(".github", "workflows", "drift.yml").Should().Contain("staging_ref");
        }

        [Test]
        public void qa_requires_sign_in_dev_does_not_and_both_accept_every_name_requestarr_is_reached_by()
        {
            var provisioning = Read("docker", "provisioning.yml");

            provisioning.Should().Contain("required: ${REQUESTARR_AUTH_REQUIRED:-disabledForLocalAddresses}").And.Contain("allowedHosts: localhost, 127.0.0.1, requestarr");
            Read("docker", "compose.yaml").Should().Contain("REQUESTARR_AUTH_REQUIRED: ${STACK_AUTH_REQUIRED:-disabledForLocalAddresses}");
            Read("docker", "dev.ps1").Should().Contain("STACK_AUTH_REQUIRED = 'enabled'").And.Contain("'DEV_AUTH_REQUIRED' 'disabledForLocalAddresses'");
            Read("docker", "dev.sh").Should().Contain("STACK_AUTH_REQUIRED=\"enabled\"").And.Contain("STACK_AUTH_REQUIRED=\"${auth_required:-${DEV_AUTH_REQUIRED:-disabledForLocalAddresses}}\"");
            Read("docs", "setup-guide.md").Should().Contain("keep **Authentication Required** on **Enabled** (beta requires sign-in for every address, so it needs no allowed hosts)");
            Read("docs", "setup-guide.md").Should().Contain("Changing the web portal password").And.Contain("REQUESTARR_ADMIN_PASSWORD");
        }

        [Test]
        public void the_local_stacks_tell_requestarr_their_environment()
        {
            Read("docker", "compose.yaml").Should().Contain($"{ReleaseEnvironment.EnvironmentVariable}: ${{REQUESTARR_STACK}}").And.Contain(ReleaseEnvironment.StagedVersionsVariable);
            Read("docker", "dev.ps1").Should().Contain("staged-versions.py").And.Contain(ReleaseEnvironment.StagedVersionsVariable);
            Read("docker", "dev.sh").Should().Contain("staged-versions.py").And.Contain(ReleaseEnvironment.StagedVersionsVariable);
            Read("docker", "staged-versions.py").Should().Contain("actions/workflows/image.yml/runs").And.Contain("branch=staging").And.Contain("status=success")
                .And.Contain("origin/staging", "offline, the staging branch is the next best record");
        }

        [Test]
        public void the_changelog_has_two_audiences_and_is_checked()
        {
            var changelog = Read("CHANGELOG.md");

            changelog.Should().NotContain("### Internal").And.NotContain("nothing this time").And.NotContain("`");
            Regex.Matches(changelog, @"^### (.+)$", RegexOptions.Multiline).Select(m => m.Groups[1].Value.Trim()).Distinct().Should().BeEquivalentTo(new[] { "For users", "For admins" });
            Changelog.Parse(changelog).SelectMany(v => v.Users.Concat(v.Admins)).Should().OnlyContain(l => l.StartsWith("**"), "each line leads with what changed, in bold");

            var check = Read(".github", "changelog_check.py");
            check.Should().Contain("def self_test").And.Contain("nothing this time").And.Contain("minor fixes");
            Read(".github", "workflows", "build.yml").Should().Contain("python3 .github/changelog_check.py --self-test");
            Read(".github", "pull_request_template.md").Should().Contain("For users").And.Contain("For admins").And.NotContain("Internal");
        }

        [Test]
        public void the_owner_has_a_server_guide_in_container_managers_order()
        {
            var guide = Read("docs", "setup-guide.md");
            var start = guide.IndexOf("## Running the beta\n");

            start.Should().BeGreaterThan(0);
            var section = guide.Substring(start, guide.IndexOf("\n## ", start + 10) - start);
            var steps = Regex.Matches(section, @"^### B\.(\d+) (?<title>.+)$", RegexOptions.Multiline).Select(m => m.Groups["title"].Value.Trim()).ToList();
            steps.Should().Equal(
                "Download the image", "Run it", "The first-run setup", "Delete files, and why it starts off", "Check Requestarr in Discord", "Updating", "Going to a specific build", "Rolling back", "With a compose file instead");
            Regex.Matches(section, @"\*\*Check:\*\*").Count.Should().BeGreaterOrEqualTo(8, "each step says what to check");

            // The registry route leads: Registry, Use, Download, then Image, Run with a name, a port, a folder and the time zone.
            section.Should().Contain("**Container Manager**").And.Contain("**Use**").And.Contain("**Download**").And.Contain("**Run**").And.Contain("Add from URL");
            section.Should().Contain("`requestarr-beta`").And.Contain("**`19797`**").And.Contain("**`/config`**").And.Contain("**`TZ`**").And.Contain("the beta image says it's beta");
            section.Should().Contain("**Update available**").And.Contain("**Create docker-compose.yml**").And.Contain("values block").And.Contain("Action → Build");
            section.Should().Contain("**Requestarr Beta (availability)**").And.Contain("**System → Backup**").And.Contain("**Restore**").And.Contain("ghcr.io/requestarr/requestarr:0.7.0004");
            section.Should().NotContain("update.sh rollback").And.NotContain("http://requestarr:9797");

            var development = Read("docs", "development.md");
            development.Should().Contain("### Published images").And.Contain("### Version posts").And.Contain("./release.sh promote-staging").And.Contain("linux/arm64");
        }
    }
}
