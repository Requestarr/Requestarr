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
    // One thing to test in a version: "- **Name:** what to do. You should see what happens." Key: its name and wording
    // ("name-in-words.3f9a2c1d"), so a result stays with the same words wherever they move (a promotion, a fold into
    // Unreleased, a build number replacing Unreleased) and new words start untested. EarlierKey is the key results were
    // recorded under before (its heading and name: "0.4.15/name-in-words", or "build-5/name-in-words" under Unreleased),
    // which a result recorded then is moved from.
    // Who checks it is on the line below the item and isn't part of its wording, so classifying an item keeps its result.
    public record QaChecklistEntry(Version Version, string Key, string Name, string Text, string EarlierKey)
    {
        public QaChecker Checker { get; init; } = QaChecker.Unclassified;
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
    }

    public record QaChecklistVersion(Version Version, IReadOnlyList<QaChecklistEntry> Items);

    // QA-CHECKLIST.md at the repo root is what admins on the QA server test before a version goes to staging; every pull
    // request adds its version's items (CI's checklist check enforces the format). It is embedded in the build.
    public static class QaChecklist
    {
        public const string ResourceName = "Requestarr.QA-CHECKLIST.md";

        private static readonly Regex Heading = new(@"^## (?:(?<unreleased>Unreleased)|(?<version>\d+\.\d+\.\d+)(?: \(\d{4}-\d{2}-\d{2}\))?)\s*$");
        private static readonly Regex Item = new(@"^- \*\*(?<name>[^*:]+):\*\* (?<text>.+)$");

        // "  - Checked automatically: the web app status page check." or
        // "  - Checked by the owner, needs human eyes: how the profile popout looks in the Discord app."
        private static readonly Regex CheckedBy = new(@"^- Checked (?:automatically: the (?<check>.+) check|by the owner, (?<reason>needs human eyes|unsure of the desired behavior|couldn't verify): (?<why>.+?))\.?$");

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
            string keyPrefix = null;
            List<QaChecklistEntry> items = null;

            foreach (var raw in (text ?? string.Empty).Split('\n'))
            {
                var line = raw.Trim();
                var heading = Heading.Match(line);

                if (heading.Success)
                {
                    current = heading.Groups["unreleased"].Success ? unreleased : Version.Parse(heading.Groups["version"].Value);
                    keyPrefix = heading.Groups["unreleased"].Success ? $"build-{unreleased?.Build}" : current.ToString();
                    items = new List<QaChecklistEntry>();

                    if (current != null)
                    {
                        versions.Add(new QaChecklistVersion(current, items));
                    }

                    continue;
                }

                if (current != null)
                {
                    AddLine(items, line, current, keyPrefix);
                }
            }

            return versions;
        }

        // An item, or who checks the item above it.
        private static void AddLine(List<QaChecklistEntry> items, string line, Version version, string keyPrefix)
        {
            var item = Item.Match(line);

            if (item.Success)
            {
                var name = item.Groups["name"].Value.Trim();
                var text = item.Groups["text"].Value.Trim();
                items.Add(new QaChecklistEntry(version, WordingKey(name, text), name, text, $"{keyPrefix}/{Slug(name)}"));
                return;
            }

            var checkedBy = CheckedBy.Match(line);

            if (checkedBy.Success && items.Count > 0)
            {
                items[^1] = items[^1] with { Checker = Checker(checkedBy) };
            }
        }

        private static QaChecker Checker(Match checkedBy) =>
            checkedBy.Groups["check"].Success
                ? new QaChecker(true, checkedBy.Groups["check"].Value.Trim(), null, null)
                : new QaChecker(false, null, checkedBy.Groups["reason"].Value, checkedBy.Groups["why"].Value.Trim());

        // The versions this server tests: QA every version staging doesn't have yet; dev only the one running (where the
        // checklist is tried out). Other servers have no checklist.
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

        public static bool HasChecklist(IReleaseEnvironment environment) => environment.VersionPosts != VersionPostMode.Standing;

        private static string Slug(string name) => Regex.Replace(name.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');

        // Eight hex digits of the wording's hash: enough to tell a checklist's items apart, short enough for a button's ID.
        private static string WordingKey(string name, string text)
        {
            var wording = Regex.Replace($"{name}: {text}", @"\s+", " ");
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(wording)));

            return $"{Slug(name)}.{hash[..8].ToLowerInvariant()}";
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
