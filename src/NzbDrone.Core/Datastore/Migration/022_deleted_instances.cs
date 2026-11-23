using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: when a Sonarr or Radarr was deleted. Its row stays, so every request that went to it still shows
    // where it went; it's gone from every list and picker.
    [Migration(22)]
    public class deleted_instances : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Backends").AddColumn("Deleted").AsDateTimeOffset().Nullable();
        }
    }
}
