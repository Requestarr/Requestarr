using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // Entries already in the audit log get when they were written: their own time, or, for an import's entries dated
    // long before, the latest time of an entry written before them, so the trim counts them from the import.
    [TestFixture]
    public class AuditRecordedMigrationFixture : MigrationTest<audit_recorded>
    {
        [Test]
        public void an_imports_old_dated_entries_count_from_when_they_were_written()
        {
            var setUp = new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);
            var askedInTheOldChannel = new DateTime(2024, 3, 5, 9, 0, 0, DateTimeKind.Utc);
            var imported = new DateTime(2026, 11, 2, 12, 0, 0, DateTimeKind.Utc);

            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "web:owner", Action = "settings.change", Timestamp = setUp });
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "discord:1 (alice)", Action = "request.imported", Timestamp = askedInTheOldChannel });
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "web:owner", Action = "import.requestrr", Timestamp = imported });
            });

            var recorded = db.Query<DateTime>("SELECT \"Recorded\" FROM \"AuditEntries\" ORDER BY \"Id\"").Select(d => d.ToUniversalTime()).ToList();

            recorded.Should().Equal(setUp, setUp, imported);
        }
    }
}
