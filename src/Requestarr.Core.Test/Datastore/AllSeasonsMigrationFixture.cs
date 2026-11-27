using System;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // An import asked for new seasons only when it asked for every season, so those requests become all-seasons ones.
    [TestFixture]
    public class AllSeasonsMigrationFixture : MigrationTest<all_seasons>
    {
        [Test]
        public void imported_every_season_requests_are_marked_all_seasons()
        {
            var at = DateTime.UtcNow;
            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Imported, every season", Status = 1, Requested = at, MonitorFutureSeasons = true, Seasons = "[1,2]", Imported = true });
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Imported, some seasons", Status = 1, Requested = at, MonitorFutureSeasons = false, Seasons = "[1]", Imported = true });
                c.Insert.IntoTable("Requests").Row(new { OwnerUserId = 1, ContentType = 1, Title = "Made here", Status = 1, Requested = at, MonitorFutureSeasons = true, Seasons = "[1]", Imported = false });
            });

            db.Query<(string Title, bool AllSeasons)>("SELECT \"Title\", \"AllSeasons\" FROM \"Requests\" ORDER BY \"Id\"")
                .Should().Equal(("Imported, every season", true), ("Imported, some seasons", false), ("Made here", false));
        }
    }
}
