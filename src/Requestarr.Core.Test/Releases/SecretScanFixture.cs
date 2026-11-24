using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore;
using Requestarr.Core.Backends;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Releases
{
    // Secrets never reach test or CI output, and the build scans each change for secrets.
    [TestFixture]
    public class SecretScanFixture
    {
        // Built at run time so the source holds no key-shaped literal for the secret scanner.
        private static readonly string ApiKey = string.Concat(Enumerable.Repeat("fake", 8));

        // As the running app has it: backend settings are stored (and traced) as JSON. The database factory sets up that
        // mapping once per process, so it's run here too: without it the trace leaves the settings out, and the test
        // would pass or fail depending on which fixtures ran before it.
        [SetUp]
        public void MapSettingsLikeTheApp()
        {
            RuntimeHelpers.RunClassConstructor(typeof(DbFactory).TypeHandle);
        }

        [Test]
        public void the_sql_trace_never_prints_backend_api_keys()
        {
            var backend = new BackendInstance { Name = "Sonarr", Settings = new BackendSettings { Url = "http://sonarr:8989", ApiKey = ApiKey } };

            var trace = SqlBuilderExtensions.GetSqlLogString("INSERT INTO Backends (\"Settings\") VALUES (@Settings)", backend);

            trace.Should().Contain("==== Begin Query Trace ====").And.Contain("http://sonarr:8989");
            trace.Should().NotContain(ApiKey);
            trace.Should().Contain("(removed)");
        }

        [Test]
        public void the_sql_trace_never_prints_tokens()
        {
            var settings = new { Settings = "{\"botToken\": \"MTIzNDU2.Ghost.Token-Value-123456789\", \"accessToken\": \"secret-access\"}" };

            var trace = SqlBuilderExtensions.GetSqlLogString("UPDATE Config SET Value = @Settings", settings);

            trace.Should().NotContain("MTIzNDU2.Ghost.Token-Value-123456789").And.NotContain("secret-access");
        }

        [TestCase("\"botToken\": \"abc.def.ghi\"")]
        [TestCase("\"apiKey\": \"0123456789abcdef\"")]
        [TestCase("\"clientSecret\":\"hush\"")]
        public void the_log_cleanser_removes_secret_json_fields(string json)
        {
            CleanseLogMessage.Cleanse(json).Should().Contain("(removed)");
        }

        [Test]
        public void the_build_scans_every_change_for_secrets_with_a_pinned_self_tested_scanner()
        {
            var workflow = File.ReadAllText(RepoFiles.Path(".github", "workflows", "build.yml"));

            workflow.Should().Contain("  secrets:").And.Contain("name: Secret scan");
            Regex.IsMatch(workflow, @"gitleaks:v\d+\.\d+\.\d+").Should().BeTrue("the scanner image is pinned to a version");
            workflow.Should().Contain("Scanner self-test").And.Contain("--redact");
            Regex.Match(workflow, @"needs: \[[^\]]*\]").Value.Should().Contain("secrets", "the required build check fails when the scan fails");
        }

        [Test]
        public void scanner_allowances_are_exact_throwaway_values_only()
        {
            var config = File.ReadAllText(RepoFiles.Path(".github", "gitleaks.toml"));
            var allowed = Regex.Matches(config, @"'''(?<rx>[^']+)'''").Select(m => m.Groups["rx"].Value).ToList();

            allowed.Should().ContainSingle();
            allowed.Should().OnlyContain(rx => !rx.Contains(".*") && !rx.Contains(@"\w") && !rx.Contains('+') && !rx.Contains('['), "allowances must be exact values, never patterns");
            Directory.EnumerateFiles(RepoFiles.Path("src", "Requestarr.Core.Test"), "*.cs", SearchOption.AllDirectories)
                .Should().Contain(f => File.ReadAllText(f).Contains(allowed[0].Replace(@"\.", ".")), "the one allowance is the fake token a test uses");

            // Ignored findings: exact fingerprints (full commit, file, rule, line) in test code only.
            var ignored = File.ReadAllLines(RepoFiles.Path(".github", "gitleaksignore")).Where(l => l.Trim().Length > 0 && !l.StartsWith('#')).ToList();
            ignored.Should().OnlyContain(l => Regex.IsMatch(l, @"^[0-9a-f]{40}:src/(NzbDrone|Requestarr)\.[A-Za-z.]+\.Test/[^:*]+\.cs:[a-z-]+:\d+$"));
            File.ReadAllText(RepoFiles.Path(".github", "workflows", "build.yml")).Should().Contain("--gitleaks-ignore-path /repo/.github/gitleaksignore");
        }

        [Test]
        public void the_secrets_file_stays_out_of_git()
        {
            File.ReadAllText(RepoFiles.Path(".gitignore")).Should().Contain("secrets.env");
        }
    }
}
