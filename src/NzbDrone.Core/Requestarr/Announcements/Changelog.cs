using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace Requestarr.Core.Announcements
{
    // One line of a version: the entry it comes from, its kind (New, Fixed or Changed) and its words as shown.
    public record ChangelogLine(string Id, ChangeKind Kind, string Text);

    // One version: its users' lines, posted to members in these exact words, and to the admin channel with its admins'
    // lines; each list New first, then Fixed, then Changed. A version with nothing for either posts nothing anywhere.
    // Date: the day it was made.
    public record ChangelogVersion(Version Version, IReadOnlyList<ChangelogLine> Users, IReadOnlyList<ChangelogLine> Admins, DateTime? Date = null)
    {
        public bool HasNews => Users.Count + Admins.Count > 0;
    }

    // A line of CHANGELOG.md's markdown under its audience and kind.
    public record ChangelogMarkdownLine(VersionAudience Audience, ChangeKind Kind, string Text);

    // A version of CHANGELOG.md's markdown; Version null for Unreleased.
    public record ChangelogMarkdownSection(Version Version, DateTime? Date, IReadOnlyList<ChangelogMarkdownLine> Lines);

    // CHANGELOG.md's markdown, as the changelog's entries write it (docs/development.md#changelog; Requestarr itself
    // reads the entries, ChangelogSource): "## 0.7.0004 (YYYY-MM-DD)" (up to 0.6.0 "x.y.z"), then "### For users" and
    // "### For admins", each with "#### New", "#### Fixed" and "#### Changed" groups of "- " lines (a build from before
    // kinds has none: its lines read as Changed); "## Unreleased" on top holds what changed since the last build sent to
    // testers (docs/development.md#versions). Tests and the live checks write the versions a server sees this way.
    public static class Changelog
    {
        public const string UsersHeading = "For users";
        public const string AdminsHeading = "For admins";
        public const string UnreleasedHeading = "Unreleased";

        // From 0.7 a version's last part is a running build number people see with four digits; before, x.y.z.
        private static readonly Version FirstBuildNumbered = new(0, 7);

        // The day in a heading's brackets, whatever follows them.
        private static readonly Regex HeadingDate = new(@"^\((?<day>\d{4}-\d{2}-\d{2})\)");

        private static readonly Dictionary<string, VersionAudience> Audiences = new() { [UsersHeading] = VersionAudience.Users, [AdminsHeading] = VersionAudience.Admins };

        // unreleased: the version a dev or QA build runs as (the next build's number), which its Unreleased lines are
        // read as; null leaves them out, as on staging and production, which only ever run numbered builds.
        public static IReadOnlyList<ChangelogVersion> Parse(string text, Version unreleased = null) =>
            ChangelogSource.FromMarkdown(text).Versions(new HashSet<string>(), unreleased);

        // Every version of the markdown, in the order written, and Unreleased (Version null) when it's there.
        public static IReadOnlyList<ChangelogMarkdownSection> Sections(string text)
        {
            var reader = new MarkdownReader();

            foreach (var line in (text ?? string.Empty).Split('\n'))
            {
                reader.Read(line.TrimEnd('\r'));
            }

            return reader.Sections;
        }

        // Reads the markdown a line at a time: a version heading starts a section, an audience heading and a kind
        // heading say what the "- " lines under them are.
        private sealed class MarkdownReader
        {
            private List<ChangelogMarkdownLine> _lines;
            private VersionAudience? _audience;
            private ChangeKind _kind = ChangeKind.Changed;

            public List<ChangelogMarkdownSection> Sections { get; } = new();

            public void Read(string line)
            {
                if (line.StartsWith("## "))
                {
                    StartVersion(line.Substring(3).Trim());
                }
                else if (line.StartsWith("### "))
                {
                    _audience = Audiences.TryGetValue(line.Substring(4).Trim(), out var audience) ? audience : null;
                    _kind = ChangeKind.Changed;
                }
                else if (line.StartsWith("#### "))
                {
                    _kind = Enum.TryParse<ChangeKind>(line.Substring(5).Trim(), out var kind) ? kind : ChangeKind.Changed;
                }
                else if (line.StartsWith("- ") && _lines != null && _audience.HasValue)
                {
                    _lines.Add(new ChangelogMarkdownLine(_audience.Value, _kind, line.Substring(2).Trim()));
                }
            }

            // Any other "## " heading ends the version before it and starts nothing.
            private void StartVersion(string heading)
            {
                var unreleased = heading == UnreleasedHeading;
                var (version, date) = unreleased ? (null, null) : ParseHeading(heading);
                _audience = null;
                _lines = version != null || unreleased ? new List<ChangelogMarkdownLine>() : null;

                if (_lines != null)
                {
                    Sections.Add(new ChangelogMarkdownSection(version, date, _lines));
                }
            }
        }

        // "x.y.z (YYYY-MM-DD)", a staging build's with the goals' builds it includes after it ("0.7.0011 (2026-12-01),
        // with 0.7.0005 to 0.7.0011"): the version, null when it isn't one, and the day, null when it's missing.
        private static (Version Version, DateTime? Date) ParseHeading(string heading)
        {
            var parts = heading.Trim().Split(' ', 2);
            var version = Version.TryParse(parts[0], out var parsed) ? parsed : null;
            var dated = parts.Length > 1 ? HeadingDate.Match(parts[1]) : Match.Empty;
            DateTime? date = dated.Success && DateTime.TryParseExact(dated.Groups["day"].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day)
                ? day
                : null;

            return (version, date);
        }

        // The version a build announces: major.minor.build (.NET's revision is left out).
        public static Version Release(Version build) => new(build.Major, build.Minor, Math.Max(build.Build, 0));

        // How people see a version: 0.7.0004 from 0.7 on (.NET reads it as 0.7.4), the old 0.6.0 before.
        public static string Display(Version version)
        {
            var release = Release(version);
            var buildFormat = release >= FirstBuildNumbered ? "D4" : "D";

            return $"{release.Major}.{release.Minor}.{release.Build.ToString(buildFormat, CultureInfo.InvariantCulture)}";
        }
    }

    // The versions a server has announced, stored as "0.2.1,0.2.3,0.2.4". Every version up to the lowest one counts as
    // announced too: a server records only its current version on its first start, and before this list the field
    // held just the highest version announced. Versions don't arrive in order: a fix made in QA takes a number above
    // dev's, and the dev versions it skipped reach QA later.
    public sealed class Announced
    {
        private readonly SortedSet<Version> _versions;

        private Announced(IEnumerable<Version> versions)
        {
            _versions = new SortedSet<Version>(versions);
        }

        public bool IsEmpty => _versions.Count == 0;

        public static Announced Parse(string text)
        {
            var versions = (text ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
                .Where(v => v != null);

            return new Announced(versions);
        }

        public bool Contains(Version version) => !IsEmpty && (version <= _versions.Min || _versions.Contains(version));

        public Announced With(IEnumerable<Version> versions) => new(_versions.Concat(versions));

        public override string ToString() => string.Join(",", _versions);
    }

    // QA's and dev's record of the version cards each channel got, stored as "channel:0.7.5,0.7.6;channel:0.7.6". Kept
    // per channel, so a card not posted in one channel yet (no lines for users then, no announcements channel) is still
    // posted there once, and a channel made again gets its cards again.
    public sealed class PostedCards
    {
        private readonly SortedDictionary<ulong, SortedSet<Version>> _channels = new();

        public static PostedCards Parse(string text)
        {
            var posted = new PostedCards();

            foreach (var channel in (text ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = channel.Split(':');

                if (parts.Length != 2 || !ulong.TryParse(parts[0], out var id))
                {
                    continue;
                }

                foreach (var version in parts[1].Split(',', StringSplitOptions.RemoveEmptyEntries))
                {
                    if (Version.TryParse(version, out var parsed))
                    {
                        posted.Add(id, parsed);
                    }
                }
            }

            return posted;
        }

        public bool Has(ulong channel, Version version) => _channels.TryGetValue(channel, out var versions) && versions.Contains(version);

        public void Add(ulong channel, Version version)
        {
            if (!_channels.TryGetValue(channel, out var versions))
            {
                versions = new SortedSet<Version>();
                _channels[channel] = versions;
            }

            versions.Add(version);
        }

        public override string ToString() => string.Join(";", _channels.Select(c => $"{c.Key}:{string.Join(",", c.Value)}"));
    }
}
