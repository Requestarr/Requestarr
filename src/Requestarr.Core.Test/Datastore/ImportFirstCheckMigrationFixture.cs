using System;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // Requests imported before the upgrade that are still followed get a first check that tells nobody; no summary is due.
    [TestFixture]
    public class ImportFirstCheckMigrationFixture : MigrationTest<import_first_check>
    {
        [Test]
        public void requests_imported_before_and_still_followed_are_due_a_quiet_first_check()
        {
            var at = DateTime.UtcNow;
            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Followed", Status = 1, ExternalItemId = 7, Requested = at, MonitorFutureSeasons = false, Seasons = "[]", Imported = true });
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Available", Status = 5, ExternalItemId = 8, Requested = at, MonitorFutureSeasons = false, Seasons = "[]", Imported = true });
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Made here", Status = 1, ExternalItemId = 9, Requested = at, MonitorFutureSeasons = false, Seasons = "[]", Imported = false });
            });

            db.Query<(string Title, bool FirstCheckDue, bool SummaryDue)>("SELECT \"Title\", \"FirstCheckDue\", \"SummaryDue\" FROM \"Requests\" ORDER BY \"Id\"")
                .Should().Equal(("Followed", true, false), ("Available", false, false), ("Made here", false, false));
        }
    }
}
