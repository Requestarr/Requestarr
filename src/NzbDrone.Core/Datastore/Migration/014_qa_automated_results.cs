using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the automated checks' results on the QA checklist carry their evidence: what its check read back, and the
    // screenshots it took, which the item's card shows.
    [Migration(14)]
    public class qa_automated_results : NzbDroneMigrationBase
    {
        protected override void MainDbUpgrade()
        {
            Alter.Table("QaChecklistItems").AddColumn("Evidence").AsString().Nullable();
            Alter.Table("QaChecklistItems").AddColumn("EvidenceFiles").AsString().Nullable();
        }
    }
}
