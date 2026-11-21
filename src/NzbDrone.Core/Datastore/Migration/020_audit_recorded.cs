using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: when each audit entry was written, which the audit log's trim goes by, not when what it describes
    // happened (an import writes entries dated in the old channel, which the trim deleted). Entries already there get the
    // latest time of any entry written before them, so an import's entries count from the import, not from the old
    // channel's dates.
    [Migration(20)]
    public class audit_recorded : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("AuditEntries").AddColumn("Recorded").AsDateTimeOffset().Nullable();
            Execute.Sql("UPDATE \"AuditEntries\" SET \"Recorded\" = (SELECT MAX(\"Earlier\".\"Timestamp\") FROM \"AuditEntries\" AS \"Earlier\" WHERE \"Earlier\".\"Id\" <= \"AuditEntries\".\"Id\")");
        }
    }
}
