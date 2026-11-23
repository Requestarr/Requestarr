using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // An earlier import's requests are marked imported (found by their audit entry), so the Import page can post them
    // later; the upgrade itself queues nothing.
    [TestFixture]
    public class HistoryPostsMigrationFixture : MigrationTest<history_posts>
    {
        [Test]
        public void earlier_imports_are_marked_and_nothing_is_queued()
        {
            var at = DateTime.UtcNow;
            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Imported", Status = 5, Requested = at, MonitorFutureSeasons = false, Seasons = "[]" });
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Made here", Status = 5, Requested = at, MonitorFutureSeasons = false, Seasons = "[]" });
                c.Insert.IntoTable("AuditEntries").Row(new { Timestamp = at, Actor = "discord:1", Action = "request.imported", TargetType = "request", TargetId = "1" });
            });

            db.Query<(string Title, bool Imported)>("SELECT \"Title\", \"Imported\" FROM \"Requests\" ORDER BY \"Id\"").Should().Equal(("Imported", true), ("Made here", false));
            db.Query<int>("SELECT COUNT(*) FROM \"HistoryPosts\"").Single().Should().Be(0);
        }
    }
}
