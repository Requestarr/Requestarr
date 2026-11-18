using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Announcements;

namespace Requestarr.Core.Qa
{
    public static class QaRoutes
    {
        // Checklist card buttons: "qacheck:pass:{item key}", "qacheck:fail:{item key}".
        public const string Check = "qacheck";

        // Bug reports: the panel's button (and a failing checklist item) -> the part menu -> the form.
        public const string Report = "bugreport";
        public const string Part = "bugpart";
        public const string Form = "bugform";
        public const string WhatField = "what";
        public const string ExpectedField = "expected";
    }

    // After a promotion: the items that kept the pass an earlier build got, and the items new (or reworded) in this one.
    public record QaCarriedOver(int Carried, int New);

    // Who checks what: the automated items and how they stand, and the owner's items still to test (by name).
    public record QaCheckers(int AutomatedItems, int AutomatedPassed, int AutomatedFailing, int OwnerItems, IReadOnlyList<string> OwnerLeft);

    public static class QaText
    {
        public const uint ChecklistColor = 0xE0A030;
        public const uint PassedColor = 0x2EA043;
        public const uint FailingColor = 0xDA3633;
        public const uint ReportColor = 0x5865F2;

        // A card shows one short line of what the automated check read back (the screenshots show under it); the whole
        // read-back is in the record and the task's reply.
        public const int EvidenceLength = 200;

        public const string ChecklistChannelName = "requestarr-qa-checklist";
        public const string ChecklistChannelTopic = "What to test on this server before the versions here go to staging. Press Passes or Failing on each item.";
        public const string BugReportsChannelName = "requestarr-bug-reports";
        public const string BugReportsChannelTopic = "Found a problem? Press Report a problem. Reports reach the people who build Requestarr.";
        public const string ChecklistTitle = "## QA checklist";

        public const string PassLabel = "Passes";
        public const string FailLabel = "Failing";
        public const string ReportLabel = "Report a problem";
        public const string PartPlaceholder = "Which part of Requestarr?";
        public const string FormTitle = "Report a problem";
        public const string WhatLabel = "What happened?";
        public const string ExpectedLabel = "What did you expect instead?";

        public const string AdminsOnly = "Only admins can mark checklist items.";
        public const string ItemGone = "That item isn't on the checklist any more.";
        public const string NoChannel = "The bug reports channel isn't set up yet. Try again in a minute.";
        public const string Sending = "Sending your report…";

        // lastPassedBy: how the last item was passed, as By says it.
        public static string Summary(IReadOnlyList<Version> versions, int total, int passed, int failing, QaCarriedOver carriedOver, QaCheckers checkers, string lastPassedBy, DateTime? completedAt)
        {
            var which = versions.Count switch
            {
                0 => "nothing to test",
                1 => $"version {Changelog.Display(versions[0])}",
                _ => $"versions {string.Join(", ", versions.Select(Changelog.Display))}"
            };

            return $"{ChecklistTitle}\nWhat to test here before {which} go{(versions.Count == 1 ? "es" : string.Empty)} to staging: **{passed} of {total} passed**{(failing > 0 ? $", {failing} failing" : string.Empty)}.\n" +
                   AutomatedLine(checkers) +
                   OwnerLine(checkers) +
                   Carried(carriedOver) +
                   ChecklistStatus(total, passed, failing, lastPassedBy, completedAt);
        }

        private static string AutomatedLine(QaCheckers checkers)
        {
            if (checkers.AutomatedItems == 0)
            {
                return string.Empty;
            }

            var failing = checkers.AutomatedFailing > 0 ? $", {checkers.AutomatedFailing} failing" : string.Empty;
            return $"🤖 **Automated checks:** {checkers.AutomatedPassed} of {checkers.AutomatedItems} passed{failing}, each with what it checked on the card.\n";
        }

        private static string OwnerLine(QaCheckers checkers)
        {
            if (checkers.OwnerItems == 0)
            {
                return string.Empty;
            }

            return checkers.OwnerLeft.Count == 0
                ? $"👤 **For you:** all {checkers.OwnerItems} passed.\n"
                : $"👤 **For you:** {checkers.OwnerLeft.Count} of {checkers.OwnerItems} left: {string.Join(", ", checkers.OwnerLeft)}.\n";
        }

        private static string Carried(QaCarriedOver carriedOver) =>
            carriedOver.Carried > 0 ? $"🔁 **{carriedOver.Carried} carried over** with their pass from an earlier build; **{carriedOver.New} new or changed** in this one.\n" : string.Empty;

        private static string ChecklistStatus(int total, int passed, int failing, string lastPassedBy, DateTime? completedAt)
        {
            if (total == 0)
            {
                return "Nothing to test by hand in these versions.";
            }

            if (failing > 0)
            {
                return $"❌ **Can't complete:** {failing} item{(failing == 1 ? " is" : "s are")} failing. Fix, then press Passes once it works.";
            }

            if (passed != total)
            {
                return $"⏳ **{total - passed} left to test.**";
            }

            var last = lastPassedBy != null ? $", the last {lastPassedBy} {Stamp(completedAt)}" : string.Empty;
            return $"✅ **Complete:** every item passed{last}. Staging can have these versions once no bug report from QA is still open.";
        }

        public static string Item(QaChecklistEntry entry, QaChecklistItem record, BugReport report)
        {
            var state = record?.State switch
            {
                QaItemState.Passed => $"✅ Passed {By(record)} {Stamp(record.At)}",
                QaItemState.Failing => $"❌ Failing, reported {By(record)} {Stamp(record.At)}{(report != null ? $": {ReportLink(report)}" : string.Empty)}",
                _ => "⏳ Not tested yet"
            };

            return $"**{entry.Name}** · {Changelog.Display(entry.Version)}\n{entry.Text}\n{CheckedBy(entry.Checker)}\n{state}{Evidence(record)}";
        }

        private static string CheckedBy(QaChecker checker) => checker switch
        {
            { Automated: true } => $"-# 🤖 Checked automatically by the {checker.Check} check.",
            { Why: null } => $"-# 👤 For you to check ({checker.Reason}).",
            _ => $"-# 👤 For you to check, {checker.Reason}: {checker.Why}"
        };

        private static string Evidence(QaChecklistItem record) =>
            string.IsNullOrWhiteSpace(record?.Evidence) ? string.Empty : $"\n**What the check saw:** {Plain(Shorten(FirstLine(record.Evidence), EvidenceLength))}";

        // "automatically", or "by" the admin who did it.
        public static string By(QaChecklistItem record) => QaChecker.IsAutomation(record) ? "automatically" : $"by {record.ByName}";

        private static string FirstLine(string text) => text.Trim().Split('\n')[0].Trim();

        private static string Shorten(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 1) + "…";

        public static string BugReportsPanel(bool filed) =>
            "## Report a problem\nSomething not working the way it should? Press **Report a problem**, pick the part of Requestarr, and say what " +
            "happened and what you expected. It goes to the people who build Requestarr, with this server's version." +
            (filed ? "\n\n**Reports are public:** each one becomes an issue on GitHub that anyone can read. Don't include anything private." : string.Empty);

        public static string PartPrompt(bool filed, string itemName) =>
            (itemName != null ? $"Reporting the failing checklist item **{itemName}**. " : string.Empty) +
            "Pick the part of Requestarr it's about, then describe it." +
            (filed
                ? "\n\n**This report will be public:** it becomes an issue on GitHub that anyone can read, with your Discord name. Don't include anything private."
                : "\n\nYour report is posted in the bug reports channel with your Discord name.");

        public static string Wait(TimeSpan wait) => $"You've sent a report just now. You can send another {In(wait)}.";

        public static string Thanks(BugReport report, ulong channelId) =>
            $"Thanks, your report is in <#{channelId}>{(report.IssueUrl != null ? $" and on GitHub: {report.IssueUrl}" : ".")}";

        public static string ReportCard(BugReport report) =>
            $"**Bug report #{report.Id}** · {BugReportComponents.NameOf(report.Component)}\n" +
            $"**What happened:** {Plain(report.WhatHappened)}\n" +
            (string.IsNullOrWhiteSpace(report.Expected) ? string.Empty : $"**Expected:** {Plain(report.Expected)}\n") +
            $"-# {EnvironmentName(report.Environment)} · Requestarr {report.Version} · <t:{Unix(report.Created)}:f> · reported by {Plain(report.ReporterName)}\n" +
            Filing(report);

        // Where the report is, or exactly why it isn't on GitHub, so an admin can act on it.
        public static string Filing(BugReport report) => report.Filing switch
        {
            BugReportFiling.Filed => $"🔗 On GitHub as issue #{report.IssueNumber}: {report.IssueUrl}",
            BugReportFiling.Pending => "⏳ Being filed on GitHub…",
            BugReportFiling.Failed => $"⚠️ Not on GitHub yet: {report.FilingProblem ?? "GitHub refused it or couldn't be reached"}. Requestarr tries again each time it starts.",
            BugReportFiling.NoToken => "⚠️ Not on GitHub yet: this server has no GitHub access key. It's filed once the key is added and Requestarr restarts.",
            _ => "Not filed on GitHub: filing reports is turned off in this server's settings. The admins have it here."
        };

        public static string IssueTitle(BugReport report)
        {
            var what = report.WhatHappened.Replace('\n', ' ').Trim();
            return $"[{EnvironmentName(report.Environment)}] {BugReportComponents.NameOf(report.Component)}: {(what.Length > 70 ? what.Substring(0, 67) + "..." : what)}";
        }

        // Only what the reporter typed and what Requestarr adds: the environment, version, time, part and their Discord name.
        public static string IssueBody(BugReport report, QaChecklistEntry item) =>
            $"**What happened**\n\n{NoPings(report.WhatHappened)}\n\n" +
            (string.IsNullOrWhiteSpace(report.Expected) ? string.Empty : $"**What they expected**\n\n{NoPings(report.Expected)}\n\n") +
            (item != null ? $"**From the QA checklist:** {item.Name} ({item.Version})\n\n" : string.Empty) +
            "| | |\n|---|---|\n" +
            $"| Part | {BugReportComponents.NameOf(report.Component)} |\n" +
            $"| Environment | {EnvironmentName(report.Environment)} |\n" +
            $"| Version | {report.Version} |\n" +
            $"| Reported | {report.Created:yyyy-MM-dd HH:mm} UTC |\n" +
            $"| Reporter (Discord) | {NoPings(report.ReporterName)} |\n\n" +
            $"_Sent with Requestarr's Report a problem button (report #{report.Id} on that server)._";

        public static IReadOnlyList<string> IssueLabels(BugReport report)
        {
            var labels = new List<string> { "bug report", $"env: {report.Environment}", $"version: {report.Version}", $"component: {report.Component}" };

            // The dev server tries the feature out: its issues are marked as tests.
            if (report.Environment == "dev")
            {
                labels.Add("test");
            }

            return labels;
        }

        public static string EnvironmentName(string iconSet) => iconSet switch
        {
            "dev" => "Dev",
            "qa" => "QA",
            "beta" => "Beta",
            _ => "Production"
        };

        private static string Stamp(DateTime? at) => at.HasValue ? $"<t:{Unix(at.Value)}:R>" : string.Empty;

        // Stored times are UTC.
        private static long Unix(DateTime at) => new DateTimeOffset(DateTime.SpecifyKind(at, DateTimeKind.Utc)).ToUnixTimeSeconds();

        private static string ReportLink(BugReport report) => report.IssueUrl != null ? $"[bug report #{report.Id}]({report.IssueUrl})" : $"bug report #{report.Id}";

        // What someone typed, shown in a card: no mentions (no pings) and no headings.
        private static string Plain(string text) => NoPings(text).Replace("\n#", "\n\\#");

        // An @ followed by an invisible space: no Discord mention, and nobody on GitHub is notified.
        private static string NoPings(string text) => (text ?? string.Empty).Replace("@", "@​");

        private static string In(TimeSpan wait) => wait switch
        {
            { TotalMinutes: < 1 } => "in a moment",
            { TotalHours: < 1 } => $"in {Math.Ceiling(wait.TotalMinutes)} minutes",
            _ => $"in {Math.Ceiling(wait.TotalHours)} hours"
        };
    }
}
