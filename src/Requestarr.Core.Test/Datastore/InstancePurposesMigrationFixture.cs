using System;
using System.Linq;
using System.Text.Json.Nodes;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // Each type's Default (or, with none ticked, its first one turned on) becomes the instance for everything; every
    // other one is for nothing yet, and members' own instances go.
    [TestFixture]
    public class InstancePurposesMigrationFixture : MigrationTest<instance_purposes>
    {
        [Test]
        public void the_default_becomes_everything_the_rest_nothing_and_member_instances_go()
        {
            var at = DateTime.UtcNow;
            var db = WithDapperMigrationTestDb(c =>
            {
                c.Insert.IntoTable("Backends").Row(new { Name = "Sonarr", Implementation = "Sonarr", Enable = true, Added = at, Settings = "{\"isDefault\": false}" });
                c.Insert.IntoTable("Backends").Row(new { Name = "Sonarr Anime", Implementation = "Sonarr", Enable = true, Added = at, Settings = "{\"isDefault\": true}" });
                c.Insert.IntoTable("Backends").Row(new { Name = "Radarr off", Implementation = "Radarr", Enable = false, Added = at, Settings = "{}" });
                c.Insert.IntoTable("Backends").Row(new { Name = "Radarr", Implementation = "Radarr", Enable = true, Added = at, Settings = "{}" });
                c.Insert.IntoTable("UserMediaAccess").Row(new { UserId = 1, ContentType = 0, State = 2, BackendId = 4 });
            });

            var purposes = db.Query<(string Name, string Settings)>("SELECT \"Name\", \"Settings\" FROM \"Backends\" ORDER BY \"Id\"")
                .ToDictionary(r => r.Name, r => JsonNode.Parse(r.Settings)!.AsObject());

            purposes.ToDictionary(p => p.Key, p => (string)p.Value["purpose"]).Should().Equal(new System.Collections.Generic.Dictionary<string, string>
            {
                ["Sonarr"] = "nothing",
                ["Sonarr Anime"] = "everything",
                ["Radarr off"] = "nothing",
                ["Radarr"] = "everything"
            });
            purposes.Values.Should().OnlyContain(s => !s.ContainsKey("isDefault"));

            db.Query<string>("SELECT name FROM pragma_table_info('UserMediaAccess')").Should().NotContain("BackendId");
            db.Query<int>("SELECT \"State\" FROM \"UserMediaAccess\"").Should().Equal(2);
        }
    }
}
