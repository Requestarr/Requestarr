using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Requestarr.Core.Announcements
{
    public enum ChangeKind
    {
        New,
        Fixed,
        Changed
    }

    // One change as its entry file says it (changelog/entries/<id>.txt, docs/development.md#changelog). Build: null until
    // the staging promotion gives it one. LineIfSeen: what changed, for readers who already got an entry it replaces.
    public record ChangelogEntry(string Id, VersionAudience Audience, ChangeKind Kind, Version Build, IReadOnlyList<string> Replaces, string Line, string LineIfSeen);

    // The changelog as entries and the builds they shipped in, embedded in the build. What a server shows is folded
    // against what it has already posted (Versions): an entry another one replaces is left out where it was never posted,
    // and the one replacing it shows in its final form, as New when the replaced one was New; where it was posted, the
    // replacing entry shows what changed (its LineIfSeen).
    public class ChangelogSource
    {
        public const string EntryResourcePrefix = "Requestarr.changelog.entries.";
        public const string BuildsResource = "Requestarr.changelog.builds.txt";

        private static readonly Dictionary<string, VersionAudience> Audiences = new() { ["users"] = VersionAudience.Users, ["admins"] = VersionAudience.Admins };
        private static readonly Dictionary<string, ChangeKind> Kinds = new() { ["new"] = ChangeKind.New, ["fixed"] = ChangeKind.Fixed, ["changed"] = ChangeKind.Changed };

        public ChangelogSource(IReadOnlyList<ChangelogEntry> entries, IReadOnlyDictionary<Version, DateTime> builds)
        {
            Entries = entries;
            Builds = builds;
        }

        public IReadOnlyList<ChangelogEntry> Entries { get; }

        // Every build sent to testers and the day it was made, entries or not.
        public IReadOnlyDictionary<Version, DateTime> Builds { get; }

        public static ChangelogSource Embedded()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var entries = assembly.GetManifestResourceNames()
                .Where(n => n.StartsWith(EntryResourcePrefix, StringComparison.Ordinal))
                .Select(n => ParseEntry(Path.GetFileNameWithoutExtension(n.Substring(EntryResourcePrefix.Length)), Read(assembly, n)))
                .Where(e => e != null)
                .ToList();

            return new ChangelogSource(entries, ParseBuilds(Read(assembly, BuildsResource)));
        }

        // A changelog as CHANGELOG.md shows it, read back as entries (one a line, its kind from its New, Fixed or Changed
        // heading): how tests and the live checks write the versions a server sees.
        public static ChangelogSource FromMarkdown(string text)
        {
            var sections = Changelog.Sections(text);
            var entries = sections.SelectMany(s => s.Lines.Select((line, i) => new ChangelogEntry(
                $"{(s.Version == null ? Changelog.UnreleasedHeading : Changelog.Display(s.Version))}-{line.Audience}-{i:D3}",
                line.Audience,
                line.Kind,
                s.Version,
                Array.Empty<string>(),
                line.Text,
                null))).ToList();

            return new ChangelogSource(entries, sections.Where(s => s.Version != null).GroupBy(s => s.Version).ToDictionary(g => g.Key, g => g.First().Date ?? DateTime.MinValue));
        }

        // Every build up to the one running (all of them when it's not given), and Unreleased read as a dev or QA server's
        // next build when one is given, each with the lines a server that already posted those entries shows, newest
        // first. Only these entries can replace others there: a later build's changes don't hide anything yet.
        public IReadOnlyList<ChangelogVersion> Versions(IReadOnlySet<string> posted, Version unreleased = null, Version upTo = null)
        {
            var byId = Entries.GroupBy(e => e.Id).ToDictionary(g => g.Key, g => g.First());
            var available = Entries.Where(e => e.Build == null ? unreleased != null : upTo == null || e.Build <= upTo).ToList();
            var replaced = available.SelectMany(e => e.Replaces).ToHashSet();
            var shown = available.Where(e => !replaced.Contains(e.Id) || posted.Contains(e.Id))
                .Select(e => (Version: e.Build ?? unreleased, e.Audience, Line: Shown(e, byId, posted)))
                .ToList();
            var versions = Builds.Keys.Where(v => upTo == null || v <= upTo).Concat(shown.Select(s => s.Version)).Distinct();

            return versions.Select(v => new ChangelogVersion(
                    v,
                    Ordered(shown.Where(s => s.Version == v && s.Audience == VersionAudience.Users).Select(s => s.Line)),
                    Ordered(shown.Where(s => s.Version == v && s.Audience == VersionAudience.Admins).Select(s => s.Line)),
                    Builds.TryGetValue(v, out var day) && day != DateTime.MinValue ? day : null))
                .OrderByDescending(v => v.Version)
                .ToList();
        }

        private static ChangelogLine Shown(ChangelogEntry entry, IReadOnlyDictionary<string, ChangelogEntry> byId, IReadOnlySet<string> posted)
        {
            var before = Ancestors(entry, byId);

            if (before.Any(posted.Contains))
            {
                return new ChangelogLine(entry.Id, entry.Kind, entry.LineIfSeen ?? entry.Line);
            }

            return new ChangelogLine(entry.Id, before.Any(id => byId[id].Kind == ChangeKind.New) ? ChangeKind.New : entry.Kind, entry.Line);
        }

        // Every entry this one replaces, through the whole chain (the changelog check refuses a chain that loops).
        private static List<string> Ancestors(ChangelogEntry entry, IReadOnlyDictionary<string, ChangelogEntry> byId)
        {
            var seen = new List<string>();
            var todo = new Queue<string>(entry.Replaces);

            while (todo.TryDequeue(out var id))
            {
                if (seen.Contains(id) || !byId.TryGetValue(id, out var found))
                {
                    continue;
                }

                seen.Add(id);

                foreach (var next in found.Replaces)
                {
                    todo.Enqueue(next);
                }
            }

            return seen;
        }

        private static IReadOnlyList<ChangelogLine> Ordered(IEnumerable<ChangelogLine> lines) =>
            lines.OrderBy(l => l.Kind).ThenBy(l => l.Id, StringComparer.Ordinal).ToList();

        // "name: value" a line; null for a file that isn't an entry (the changelog check refuses one).
        public static ChangelogEntry ParseEntry(string id, string text)
        {
            var fields = (text ?? string.Empty).Split('\n')
                .Select(row => row.TrimEnd('\r'))
                .Where(row => row.Contains(':'))
                .Select(row => row.Split(':', 2))
                .ToDictionary(parts => parts[0].Trim(), parts => parts[1].Trim());

            if (!Audiences.TryGetValue(fields.GetValueOrDefault("audience", string.Empty), out var audience) ||
                !Kinds.TryGetValue(fields.GetValueOrDefault("kind", string.Empty), out var kind) ||
                string.IsNullOrEmpty(fields.GetValueOrDefault("line")))
            {
                return null;
            }

            var build = Version.TryParse(fields.GetValueOrDefault("build", string.Empty), out var parsed) ? parsed : null;
            var replaces = fields.GetValueOrDefault("replaces", string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var seen = fields.GetValueOrDefault("line_if_seen");

            return new ChangelogEntry(id, audience, kind, build, replaces, fields["line"], string.IsNullOrEmpty(seen) ? null : seen);
        }

        // "version | YYYY-MM-DD | notes" a line, "#" lines being comments.
        public static IReadOnlyDictionary<Version, DateTime> ParseBuilds(string text)
        {
            var builds = new Dictionary<Version, DateTime>();

            foreach (var row in (text ?? string.Empty).Split('\n').Select(r => r.Trim()).Where(r => r.Length > 0 && !r.StartsWith('#')))
            {
                var parts = row.Split('|', StringSplitOptions.TrimEntries);

                if (parts.Length >= 2 && Version.TryParse(parts[0], out var version) &&
                    DateTime.TryParseExact(parts[1], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var day))
                {
                    builds[version] = day;
                }
            }

            return builds;
        }

        private static string Read(Assembly assembly, string name)
        {
            using var stream = assembly.GetManifestResourceStream(name);

            if (stream == null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
