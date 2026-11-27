using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: a TV request remembers that it asked for all seasons, so its cards say so. An import asks for new
    // seasons only when it asked for every season, so those imported requests are marked too.
    [Migration(27)]
    public class all_seasons : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("AllSeasons").AsBoolean().WithDefaultValue(false);
            Execute.Sql("UPDATE \"Requests\" SET \"AllSeasons\" = 1 WHERE \"Imported\" = 1 AND \"MonitorFutureSeasons\" = 1");
        }
    }
}
