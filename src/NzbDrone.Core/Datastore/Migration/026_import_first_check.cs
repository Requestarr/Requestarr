using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: an imported request's first check by Sonarr or Radarr records its state without telling anyone, and one
    // post sums up what needs attention. Requests imported before this get that first check after upgrading, with no post.
    [Migration(26)]
    public class import_first_check : NzbDroneMigrationBase
    {
        // Request status: Approved.
        private const int Approved = 1;

        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests")
                .AddColumn("FirstCheckDue").AsBoolean().WithDefaultValue(false)
                .AddColumn("SummaryDue").AsBoolean().WithDefaultValue(false);
            Execute.Sql($"UPDATE \"Requests\" SET \"FirstCheckDue\" = 1 WHERE \"Imported\" = 1 AND \"Status\" = {Approved} AND \"ExternalItemId\" IS NOT NULL");
        }
    }
}
