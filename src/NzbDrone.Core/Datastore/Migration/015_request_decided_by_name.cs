using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: who decided a request, by name, for an admin who isn't a member (one acting in the web app), so
    // Discord's cards can say who approved, denied or cancelled it.
    [Migration(15)]
    public class request_decided_by_name : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("DecidedByName").AsString().Nullable();
        }
    }
}
