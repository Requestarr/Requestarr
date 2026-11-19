using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: what each request's title is (overview, dates, its slug in Sonarr or Radarr), looked up once and kept,
    // so the web app's request lists show it without asking anyone each time. Older requests are filled in at start.
    [Migration(19)]
    public class request_media : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("Media").AsString().Nullable();
        }
    }
}
