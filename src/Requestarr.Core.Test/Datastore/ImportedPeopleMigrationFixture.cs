using System;
using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // People the first imports called "left the server" at the moment they were recorded were never in it; someone who
    // left later stays left. Imported people take their source channel from their first imported request.
    [TestFixture]
    public class ImportedPeopleMigrationFixture : MigrationTest<imported_people>
    {
        [Test]
        public void never_seen_people_are_not_in_this_server_and_real_departures_stay()
        {
            var recorded = new DateTime(2026, 11, 19, 21, 25, 32, DateTimeKind.Utc);
            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("RequestarrUsers").Row(new { DisplayName = "Chaz", Status = 4, Added = recorded, StatusChanged = recorded });
                c.Insert.IntoTable("RequestarrUsers").Row(new { DisplayName = "Gone", Status = 4, Added = recorded, StatusChanged = recorded.AddHours(1) });
                c.Insert.IntoTable("RequestarrUsers").Row(new { DisplayName = "Here", Status = 3, Added = recorded, StatusChanged = recorded });
                c.Insert.IntoTable("RequestarrUsers").Row(new { DisplayName = "Member", Status = 0, Added = recorded, StatusChanged = recorded });
                c.Insert.IntoTable("RequestarrUsers").Row(new { DisplayName = "Follower", Status = 4, Added = recorded, StatusChanged = recorded });
                c.Insert.IntoTable("AuditEntries").Row(new { ActorUserId = 1, Actor = "discord:1 (Chaz)", Action = "request.imported", TargetType = "request", TargetId = "1", Timestamp = recorded, Detail = "From Requestrr, #media-requests" });
                c.Insert.IntoTable("AuditEntries").Row(new { ActorUserId = 4, Actor = "discord:4 (Member)", Action = "request.imported", TargetType = "request", TargetId = "2", Timestamp = recorded, Detail = "From Requestrr, #media-requests" });
                c.Insert.IntoTable("AuditEntries").Row(new { ActorUserId = 5, Actor = "discord:5 (Follower)", Action = "request.subscribe", TargetType = "request", TargetId = "1", Timestamp = recorded, Detail = "From Requestrr" });
            });

            var people = db.Query<(string DisplayName, int Status, string ImportedFrom)>("SELECT \"DisplayName\", \"Status\", \"ImportedFrom\" FROM \"RequestarrUsers\" ORDER BY \"Id\"").ToList();

            people.Should().Equal(("Chaz", 5, "#media-requests"), ("Gone", 4, null), ("Here", 3, null), ("Member", 0, null), ("Follower", 5, "#media-requests"));
        }
    }
}
