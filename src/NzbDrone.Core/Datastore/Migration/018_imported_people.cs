using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: where an imported person's history came from ("#media-requests in The CheaZiest"), and a fix for people
    // the first imports called "left the server" who were never in it. Those were marked so at the moment they were
    // recorded (StatusChanged equals Added); someone who really left was changed later, by Discord's member event.
    // People imported before this have only their channel to go on, from their first imported request.
    [Migration(18)]
    public class imported_people : NzbDroneMigrationBase
    {
        private const int Imported = 3;
        private const int ImportedLeft = 4;
        private const int ImportedElsewhere = 5;
        private const string RequestDetail = "From Requestrr, ";

        protected override void MainDbUpgrade()
        {
            Alter.Table("RequestarrUsers").AddColumn("ImportedFrom").AsString().Nullable();

            Execute.Sql($"UPDATE \"RequestarrUsers\" SET \"Status\" = {ImportedElsewhere} WHERE \"Status\" = {ImportedLeft} AND \"StatusChanged\" = \"Added\"");

            Execute.Sql($"UPDATE \"RequestarrUsers\" SET \"ImportedFrom\" = (" +
                        $"SELECT substr(a.\"Detail\", {RequestDetail.Length + 1}) FROM \"AuditEntries\" a " +
                        $"WHERE a.\"ActorUserId\" = \"RequestarrUsers\".\"Id\" AND a.\"Action\" = 'request.imported' AND a.\"Detail\" LIKE '{RequestDetail}#%' " +
                        "ORDER BY a.\"Timestamp\" LIMIT 1) " +
                        $"WHERE \"Status\" IN ({Imported}, {ImportedLeft}, {ImportedElsewhere}) AND \"ImportedFrom\" IS NULL");
        }
    }
}
