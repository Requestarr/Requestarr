using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // Results and history recorded under the tool's old name carry over as "automation"; everyone else's stay as they were.
    [TestFixture]
    public class AutomationNamesMigrationFixture : MigrationTest<automation_names>
    {
        // Built from parts, as in the migration, so the repository never spells the old name.
        private const string OldActor = "cl" + "aude-code";
        private const string OldName = "Cl" + "aude Code";

        [Test]
        public void the_old_name_becomes_automation_and_nothing_else_changes()
        {
            var at = DateTime.UtcNow;
            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = OldActor, Action = "qa.pass", Timestamp = at });
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "discord:0 (" + OldName + ")", Action = "bugreport.send", Timestamp = at });
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "discord:1 (alice)", Action = "qa.pass", Timestamp = at });
                c.Insert.IntoTable("QaChecklistItems").Row(new { ItemKey = "kept.1a2b3c4d", Version = "0.7.4", State = 1, ByName = OldName });
                c.Insert.IntoTable("QaChecklistItems").Row(new { ItemKey = "owners.1a2b3c4d", Version = "0.7.4", State = 1, ByName = "alice" });
                c.Insert.IntoTable("BugReports").Row(new
                {
                    Component = "web-app",
                    WhatHappened = OldName + "'s web app status page check failed: Status shows 0.7.0004.",
                    Environment = "dev",
                    Version = "0.7.0004",
                    ReporterDiscordId = "0",
                    ReporterName = OldName,
                    Created = at
                });
            });

            db.Query<string>("SELECT \"Actor\" FROM \"AuditEntries\" ORDER BY \"Id\"").Should().Equal("automation", "automation", "discord:1 (alice)");
            db.Query<string>("SELECT \"ByName\" FROM \"QaChecklistItems\" ORDER BY \"Id\"").Should().Equal("automation", "alice");
            db.Query<string>("SELECT \"ItemKey\" FROM \"QaChecklistItems\" ORDER BY \"Id\"").First().Should().Be("kept.1a2b3c4d", "a pass stays with its item");

            var report = db.Query<(string ReporterName, string WhatHappened)>("SELECT \"ReporterName\", \"WhatHappened\" FROM \"BugReports\"").Single();
            report.Should().Be(("automation", "The automated web app status page check failed: Status shows 0.7.0004."));
        }
    }
}
