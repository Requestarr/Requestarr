using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Announcements;

namespace Requestarr.Core.Qa
{
    // One thing to test in a version: a bold name, its numbered Steps and the Expect bullets. Key: its name and wording
    // ("name-in-words.3f9a2c1d"), so a result stays with the same words wherever they move (a promotion, a build number
    // replacing Unreleased) and new words start untested. Text is the wording: the steps, then what to expect.
    // Who checks it is on the last line and isn't part of its wording, so classifying an item keeps its result.
    public record QaChecklistEntry(Version Version, string Key, string Name, string Text)
    {
        public QaChecker Checker { get; init; } = QaChecker.Unclassified;
        public IReadOnlyList<string> Steps { get; init; } = Array.Empty<string>();
        public IReadOnlyList<string> Expect { get; init; } = Array.Empty<string>();
    }

    // Who checks an item: the automated checks, with the check named on the item (docker/qa_checks.py), or the owner,
    // with the reason a person has to: it needs human eyes, the desired behavior is unsure, or the automation couldn't verify
    // it. An item nobody classified is the owner's until it is.
    public record QaChecker(bool Automated, string Check, string Reason, string Why)
    {
        // Recorded as who passed or failed an item; shown as "Passed automatically".
        public const string AutomationName = "automation";

        // The automation isn't a Discord member: its results are recorded under no member (and audited as itself).
        public const ulong AutomationId = 0;

        public static readonly QaChecker Unclassified = new(false, null, "not classified yet", null);

        // By its name and no member: an admin whose Discord name happens to be the same is still that admin.
        public static bool IsAutomation(QaChecklistItem record) => record.ByDiscordUserId == null && record.ByName == AutomationName;
    }

    public record QaChecklistVersion(Version Version, IReadOnlyList<QaChecklistEntry> Items);

    // QA-CHECKLIST.md at the repo root is what admins on the QA server test before a version goes to staging; every pull
    // request adds its version's items (CI's checklist check enforces the format). It is embedded in the build.
    public static class QaChecklist
    {
        public const string ResourceName = "Requestarr.QA-CHECKLIST.md";

        // An item: "- **Importing from Requestrr**", then "  - Steps:" over "    1. Open Settings.", "  - Expect:" over
        // "    - One list of past requests.", and who checks it.
        private const string StepsLine = "  - Steps:";
        private const string ExpectLine = "  - Expect:";

        private static readonly Regex Heading = new(@"^## (?:(?<unreleased>Unreleased)|(?<version>\d+\.\d+\.\d+)(?: \(\d{4}-\d{2}-\d{2}\))?)\s*$");
        private static readonly Regex Item = new(@"^- \*\*(?<name>[^*]+)\*\*$");
        private static readonly Regex Step = new(@"^    \d+\. (?<text>.+)$");
        private static readonly Regex Bullet = new(@"^    - (?<text>.+)$");

        // "  - Checked automatically: the web app status page check." or
        // "  - Checked by the owner, needs human eyes: how the profile popout looks in the Discord app."
        private static readonly Regex CheckedBy = new(@"^  - Checked (?:automatically: the (?<check>.+) check|by the owner, (?<reason>needs human eyes|unsure of the desired behavior|couldn't verify): (?<why>.+?))\.?$");

        public static string Embedded()
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);

            if (stream == null)
            {
                return string.Empty;
            }

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        // unreleased: the version a dev or QA build runs as, which the "## Unreleased" items are read as (null leaves
        // them out), the same as the changelog's.
        public static IReadOnlyList<QaChecklistVersion> Parse(string text, Version unreleased = null)
        {
            var versions = new List<QaChecklistVersion>();
            Version current = null;
            List<QaChecklistEntry> items = null;
            ItemLines item = null;

            foreach (var raw in (text ?? string.Empty).Split('\n'))
            {
                var line = raw.TrimEnd();
                var heading = Heading.Match(line.Trim());

                if (heading.Success)
                {
                    item?.AddTo(items, current);
                    item = null;
                    current = heading.Groups["unreleased"].Success ? unreleased : Version.Parse(heading.Groups["version"].Value);
                    items = new List<QaChecklistEntry>();

                    if (current != null)
                    {
                        versions.Add(new QaChecklistVersion(current, items));
                    }

                    continue;
                }

                var name = Item.Match(line);

                if (current != null && name.Success)
                {
                    item?.AddTo(items, current);
                    item = new ItemLines(name.Groups["name"].Value.Trim());
                }
                else
                {
                    item?.Add(line);
                }
            }

            item?.AddTo(items, current);
            return versions;
        }

        // An item's wording, as its key is made from: "Steps:\n1. Open Settings.\nExpect:\n- It saves." (docker/qa_checks.py
        // builds the same, to record a result on exactly the item whose words were checked).
        private static string Wording(IEnumerable<string> steps, IEnumerable<string> expect) =>
            string.Join("\n", new[] { "Steps:" }.Concat(steps.Select((s, n) => $"{n + 1}. {s}")).Append("Expect:").Concat(expect.Select(e => $"- {e}")));

        private static QaChecker Checker(Match checkedBy) =>
            checkedBy.Groups["check"].Success
                ? new QaChecker(true, checkedBy.Groups["check"].Value.Trim(), null, null)
                : new QaChecker(false, null, checkedBy.Groups["reason"].Value, checkedBy.Groups["why"].Value.Trim());

        // The versions this server tests: QA every version staging doesn't have yet; dev only the one running (its record
        // takes the automated results, and has no channel). Other servers have no checklist.
        public static IReadOnlyList<QaChecklistVersion> ForServer(IReadOnlyList<QaChecklistVersion> versions, IReleaseEnvironment environment, Version running)
        {
            var current = Changelog.Release(running);

            return environment.VersionPosts switch
            {
                VersionPostMode.NotYetStaged => versions.Where(v => v.Version <= current && !environment.Staged.Contains(v.Version)).ToList(),
                VersionPostMode.CurrentOnly => versions.Where(v => v.Version == current).ToList(),
                _ => new List<QaChecklistVersion>()
            };
        }

        // The checklist channel: the owner's QA work, on the QA server only (owner, 2026-11-20).
        public static bool HasChecklist(IReleaseEnvironment environment) => environment.VersionPosts == VersionPostMode.NotYetStaged;

        private static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        // Eight hex digits of the wording's hash: enough to tell a checklist's items apart, short enough for a button's ID.
        private static string WordingKey(string name, string text)
        {
            var wording = Regex.Replace($"{name}: {text}", @"\s+", " ");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wording)));

            return $"{Slug(name)}.{hash[..8].ToLowerInvariant()}";
        }

        // One item's lines as they're read: its steps, what to expect, and who checks it.
        private sealed class ItemLines
        {
            private readonly string _name;
            private readonly List<string> _steps = new();
            private readonly List<string> _expect = new();
            private List<string> _part;
            private QaChecker _checker = QaChecker.Unclassified;

            public ItemLines(string name) => _name = name;

            public void Add(string line)
            {
                var checkedBy = CheckedBy.Match(line);

                if (checkedBy.Success)
                {
                    _checker = Checker(checkedBy);
                    return;
                }

                if (line is StepsLine or ExpectLine)
                {
                    _part = line == StepsLine ? _steps : _expect;
                    return;
                }

                var entry = (_part == _steps ? Step : Bullet).Match(line);

                if (_part != null && entry.Success)
                {
                    _part.Add(entry.Groups["text"].Value.Trim());
                }
            }

            public void AddTo(List<QaChecklistEntry> items, Version version)
            {
                var text = Wording(_steps, _expect);
                items.Add(new QaChecklistEntry(version, WordingKey(_name, text), _name, text) { Checker = _checker, Steps = _steps, Expect = _expect });
            }
        }
    }

    public enum QaItemState
    {
        Pending = 0,
        Passed = 1,
        Failing = 2
    }

    // Requestarr's record of each checklist item on this server: who passed or failed it and when, and the bug report
    // a failing one opened. Discord shows it; the portal will too.
    public class QaChecklistItem : ModelBase
    {
        public string ItemKey { get; set; }
        public string Version { get; set; }
        public string Name { get; set; }
        public QaItemState State { get; set; }
        public string ByDiscordUserId { get; set; }
        public string ByName { get; set; }
        public DateTime? At { get; set; }
        public int? BugReportId { get; set; }
        public string MessageId { get; set; }

        // The build it was passed or failed on (0.7.0004-qa.4f2a9c1): a pass from an earlier build was carried over.
        public string RecordedOn { get; set; }

        // The build it first appeared on: an item added on the running build is new (or reworded) in it.
        public string AddedOn { get; set; }

        // The automated result: what its check read back, and the screenshots it took (files in the app data folder's
        // qa-evidence folder, shown on the item's card).
        public string Evidence { get; set; }
        public List<string> EvidenceFiles { get; set; } = new();
    }

    public interface IQaChecklistItemRepository : IBasicRepository<QaChecklistItem>
    {
        QaChecklistItem FindByKey(string itemKey);
    }

    public class QaChecklistItemRepository : BasicRepository<QaChecklistItem>, IQaChecklistItemRepository
    {
        public QaChecklistItemRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public QaChecklistItem FindByKey(string itemKey) => Query(i => i.ItemKey == itemKey).SingleOrDefault();
    }
}
