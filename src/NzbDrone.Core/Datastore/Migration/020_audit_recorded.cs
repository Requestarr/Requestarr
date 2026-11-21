using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: when each audit entry was written, which the audit log's trim goes by, not when what it describes
    // happened (an import writes entries dated in the old channel, which the trim deleted). Entries already there count
    // from this upgrade: when they were written isn't known, and none is lost to the next trim.
    [Migration(20)]
    public class audit_recorded : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AuditEntries").AddColumn("Recorded").AsDateTimeOffset().Nullable();
            Execute.Sql("UPDATE \"AuditEntries\" SET \"Recorded\" = strftime('%Y-%m-%d %H:%M:%fZ', 'now')");
        }
    }
}
