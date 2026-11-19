using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the messages an import from another request bot (Requestrr) took into account, by message ID, so a
    // second run adds nothing twice. No message text is kept.
    [Migration(17)]
    public class imported_messages : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Create.TableForModel("ImportedMessages")
                .WithColumn("MessageId").AsString().NotNullable().Unique()
                .WithColumn("ChannelId").AsString().NotNullable()
                .WithColumn("Source").AsString().NotNullable()
                .WithColumn("Imported").AsDateTimeOffset().NotNullable();
        }
    }
}
