using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Imports
{
    // A message an import already took into account (a request, a follower, a finished download, or one it skipped
    // on purpose), so running it again skips it. Only its ID and where it came from: never its text.
    public class ImportedMessage : ModelBase
    {
        public string MessageId { get; set; }
        public string ChannelId { get; set; }
        public string Source { get; set; }
        public DateTime Imported { get; set; }
    }

    public interface IImportedMessageRepository : IBasicRepository<ImportedMessage>
    {
        IReadOnlySet<ulong> MessageIds();
    }

    public class ImportedMessageRepository : BasicRepository<ImportedMessage>, IImportedMessageRepository
    {
        public ImportedMessageRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public IReadOnlySet<ulong> MessageIds() => All().Select(m => ulong.Parse(m.MessageId)).ToHashSet();
    }
}
