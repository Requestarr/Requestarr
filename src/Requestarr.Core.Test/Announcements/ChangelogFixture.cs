using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Announcements
{
    // The changelog behind every version post: how its headings and sections are read, the shipped file the build
    // embeds, the process every pull request follows (docs/development.md) and the limits that keep each version short.
    [TestFixture]
    public class ChangelogFixture
    {
        private const string Text = @"# Changelog

## 0.3.0

### For admins
- Faster startup.

## 0.2.5

## 0.2.0

### For users
- You can now follow a request from its card.
- Your panel shows your limits.

### For admins
- New member card layout.

## 0.1.0

### For users
- Request movies and TV.
";

        [Test]
        public void the_changelog_parses_into_versions_and_sections()
        {
            var versions = Changelog.Parse(Text);

            versions.Select(v => v.Version.ToString()).Should().Equal("0.3.0", "0.2.5", "0.2.0", "0.1.0");
            versions[2].Users.Select(l => l.Text).Should().Equal("You can now follow a request from its card.", "Your panel shows your limits.");
            versions[2].Admins.Select(l => l.Text).Should().Equal("New member card layout.");
            versions[0].Users.Should().BeEmpty();
            versions[1].HasNews.Should().BeFalse("a version with nothing for users or admins has only its heading");
        }

        // A staging heading also names the goal builds it covers; an older heading has no day and still parses.
        [TestCase("## 0.2.0 (2026-10-22)", "0.2.0", "2026-10-22")]
        [TestCase("## 0.7.0011 (2026-12-01), with 0.7.0005 to 0.7.0011", "0.7.11", "2026-12-01")]
        [TestCase("## 0.1.0", "0.1.0", null)]
        public void a_heading_gives_the_version_and_the_day_it_was_made(string heading, string version, string day)
        {
            var parsed = Changelog.Parse($"# Changelog\n\n{heading}\n\n### For users\n- **Numbers:** shown.\n").Single();

            parsed.Version.Should().Be(new Version(version));
            parsed.Date.Should().Be(day == null ? null : DateTime.Parse(day, CultureInfo.InvariantCulture));
        }

        [Test]
        public void the_shipped_changelog_is_embedded_and_the_build_number_is_stamped_when_built()
        {
            var file = File.ReadAllText(RepoFiles.Path("CHANGELOG.md"));
            var props = File.ReadAllText(RepoFiles.Path("src", "Directory.Build.props"));
            var versions = Changelog.Parse(file);

            props.Should().Contain("<RequestarrVersion Condition=\"'$(RequestarrVersion)' == ''\">0.0.0</RequestarrVersion>", "a build without a number is 0.0.0");
            Regex.IsMatch(props, @"<AssemblyVersion>\d").Should().BeFalse("the number is stamped at build time, never kept in the source");
            versions.Should().NotBeEmpty();
            Changelog.Parse(file, new Version(9, 9, 9)).Count.Should().BeInRange(versions.Count, versions.Count + 1, "Unreleased, when there is one, is the build a dev or QA server runs");
            versions.Select(v => v.Version).Should().OnlyHaveUniqueItems();
            versions.Should().Contain(v => v.HasNews);
            var embedded = ChangelogSource.Embedded();
            var released = embedded.Entries.Where(e => e.Build != null).Select(e => e.Id).ToHashSet();
            Shown(embedded.Versions(released, new Version(9, 9, 9))).Should().BeEquivalentTo(Shown(Changelog.Parse(file, new Version(9, 9, 9))), "the build embeds the entries the file is written from");
            embedded.Entries.Should().HaveCount(Directory.GetFiles(RepoFiles.Path("changelog", "entries"), "*.txt").Length);
        }

        [Test]
        public void the_process_and_the_channel_are_documented()
        {
            File.ReadAllText(RepoFiles.Path("docs", "development.md")).Should().Contain("## Changelog").And.Contain("Add its lines under `## Unreleased`").And.Contain("### For users");
            File.ReadAllText(RepoFiles.Path(".github", "pull_request_template.md")).Should().Contain("CHANGELOG.md");
            File.ReadAllText(RepoFiles.Path(".github", "workflows", "build.yml")).Should().Contain("name: Changelog").And.Contain("secrets, changelog, promotion]");
            File.ReadAllText(RepoFiles.Path("docs", "setup-guide.md")).Should().Contain("### 5.8 The announcements channel").And.Contain("**Announce to users**").And.Contain("Requestarr Members");
            File.ReadAllText(RepoFiles.Path("docs", "commands.md")).Should().Contain("### Announcements");
        }

        [Test]
        public void user_lines_are_in_plain_words()
        {
            var technical = new[] { "Sonarr", "Radarr", "Lidarr", "API", "webhook", "database", "migration", ".NET", "refactor", "provisioning", "endpoint", "CI" };

            foreach (var line in Changelog.Parse(File.ReadAllText(RepoFiles.Path("CHANGELOG.md"))).SelectMany(v => v.Users.Select(l => l.Text)))
            {
                technical.Where(word => Regex.IsMatch(line, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase)).Should().BeEmpty($"\"{line}\" is for users");
            }
        }

        // A build lists every change however many there are (no cap), so a long one's Read changes carries on in more
        // messages, each within Discord's limits.
        [Test]
        public void every_released_version_reads_within_discords_limits_however_long()
        {
            foreach (var version in ChangelogSource.Embedded().Versions(new HashSet<string>()).Where(v => v.HasNews && v.Date != null))
            {
                foreach (var reply in VersionCards.Full(version, VersionAudience.Admins))
                {
                    reply.Cards.Should().HaveCountLessThanOrEqualTo(VersionCards.CardsPerMessage);
                    reply.Cards.Sum(c => c.Text.Length).Should().BeLessThanOrEqualTo(VersionCards.MessageTextLength, $"{version.Version} fits Discord's limits");
                }
            }
        }

        // Each version's lines as words, wherever the version comes from.
        private static IEnumerable<string> Shown(IEnumerable<ChangelogVersion> versions) =>
            versions.SelectMany(v => v.Users.Concat(v.Admins).Select(l => $"{v.Version} {l.Text}"));
    }
}
