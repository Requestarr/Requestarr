using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Imports
{
    // Where an imported request's history is posted: the admin feed, or its requester's own channel.
    public enum HistoryDestination
    {
        Feed = 0,
        Channel = 1
    }

    // One imported request's post to one destination: queued, then posted. A request has at most one per destination
    // (a unique index), so re-running an import, restarting or "Post history now" never posts it twice.
    public class HistoryPost : ModelBase
    {
        public int RequestId { get; set; }
        public HistoryDestination Destination { get; set; }
        public DateTime Queued { get; set; }
        public DateTime? Posted { get; set; }
        public string MessageId { get; set; }
    }

    public interface IHistoryPostRepository : IBasicRepository<HistoryPost>
    {
        List<HistoryPost> Waiting();

        // The request IDs that already have a post queued or done for the destination.
        IReadOnlySet<int> Known(HistoryDestination destination);
    }

    public class HistoryPostRepository : BasicRepository<HistoryPost>, IHistoryPostRepository
    {
        public HistoryPostRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<HistoryPost> Waiting() => Query(p => p.Posted == null).ToList();

        public IReadOnlySet<int> Known(HistoryDestination destination) => Query(p => p.Destination == destination).Select(p => p.RequestId).ToHashSet();
    }
}
