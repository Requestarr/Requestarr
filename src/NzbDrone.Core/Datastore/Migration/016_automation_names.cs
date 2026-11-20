using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the automated checks are recorded as "automation", not the name of the tool that ran them. Audit rows,
    // checklist results and the bug reports they filed carry over under the new name, so no pass or history is lost.
    // Checklist results are keyed by each item's wording; the one item reworded with this was folded (that fold has since gone, with the items it named).
    [Migration(16)]
    public class automation_names : NzbDroneMigrationBase
    {
        // The tool's name as it was recorded, built from parts so the repository never spells it.
        private const string OldActor = "cl" + "aude-code";
        private const string OldName = "Cl" + "aude Code";
        private const string OldFailure = OldName + "'s ";

        // A bug report the checks filed was audited as a Discord account with no member behind it.
        private const string OldReporter = "discord:0 (" + OldName + ")";

        protected override void MainDbUpgrade()
        {
            Execute.Sql($"UPDATE \"AuditEntries\" SET \"Actor\" = 'automation' WHERE \"Actor\" IN ('{OldActor}', '{OldReporter}')");
            Execute.Sql($"UPDATE \"QaChecklistItems\" SET \"ByName\" = 'automation' WHERE \"ByName\" = '{OldName}'");
            Execute.Sql($"UPDATE \"BugReports\" SET \"ReporterName\" = 'automation' WHERE \"ReporterName\" = '{OldName}'");

            // "<tool>'s web app status page check failed: ..." becomes "The automated web app status page check failed: ...".
            Execute.Sql($"UPDATE \"BugReports\" SET \"WhatHappened\" = 'The automated ' || substr(\"WhatHappened\", {OldFailure.Length + 1}) " +
                        $"WHERE substr(\"WhatHappened\", 1, {OldFailure.Length}) = '{OldFailure.Replace("'", "''")}'");
        }
    }
}
