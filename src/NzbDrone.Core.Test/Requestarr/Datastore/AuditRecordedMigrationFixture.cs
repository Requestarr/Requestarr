using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // Entries already in the audit log count from the upgrade, so an import's entries dated years before aren't lost to the
    // next trim.
    [TestFixture]
    public class AuditRecordedMigrationFixture : MigrationTest<audit_recorded>
    {
        [Test]
        public void entries_already_there_count_from_the_upgrade()
        {
            var askedInTheOldChannel = new DateTime(2024, 3, 5, 9, 0, 0, DateTimeKind.Utc);

            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "discord:1 (alice)", Action = "request.imported", Timestamp = askedInTheOldChannel });
                c.Insert.IntoTable("AuditEntries").Row(new { Actor = "web:owner", Action = "import.requestrr", Timestamp = DateTime.UtcNow });
            });

            var recorded = db.Query<DateTime>("SELECT \"Recorded\" FROM \"AuditEntries\" ORDER BY \"Id\"").Select(d => d.ToUniversalTime()).ToList();

            recorded.Should().HaveCount(2).And.OnlyContain(d => d > DateTime.UtcNow.AddMinutes(-5));
        }
    }
}
