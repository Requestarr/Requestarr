using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: imported history posted in Discord. Requests.Imported marks what an import wrote (earlier imports found
    // by their "request.imported" audit entry); HistoryPosts records each imported request's post per destination (the
    // admin feed, or its requester's channel), queued then posted, so nothing is ever posted twice.
    [Migration(25)]
    public class history_posts : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("Requests").AddColumn("Imported").AsBoolean().WithDefaultValue(false);
            Execute.Sql("UPDATE \"Requests\" SET \"Imported\" = 1 WHERE CAST(\"Id\" AS TEXT) IN " +
                        "(SELECT \"TargetId\" FROM \"AuditEntries\" WHERE \"Action\" = 'request.imported' AND \"TargetType\" = 'request')");

            Create.TableForModel("HistoryPosts")
                .WithColumn("RequestId").AsInt32()
                .WithColumn("Destination").AsInt32()
                .WithColumn("Queued").AsDateTimeOffset()
                .WithColumn("Posted").AsDateTimeOffset().Nullable()
                .WithColumn("MessageId").AsString().Nullable();

            Create.Index("IX_HistoryPosts_RequestId_Destination").OnTable("HistoryPosts")
                .OnColumn("RequestId").Ascending()
                .OnColumn("Destination").Ascending()
                .WithOptions().Unique();
        }
    }
}
