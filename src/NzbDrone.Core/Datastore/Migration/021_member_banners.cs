using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: each person's Discord profile banner and accent colour, for their page's header in the web app, read from
    // Discord with their names and picture.
    [Migration(21)]
    public class member_banners : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("UserIdentities").AddColumn("BannerUrl").AsString().Nullable();
            Alter.Table("UserIdentities").AddColumn("AccentColor").AsString().Nullable();
        }
    }
}
