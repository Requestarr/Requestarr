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

    // One imported request's post to one destination's channel: queued, then posted. A request has at most one per
    // destination and channel (a unique index), so re-running an import, restarting or posting again never posts it
    // twice, while a new server's feed or a member's new channel there still gets it.
    public class HistoryPost : ModelBase
    {
        public int RequestId { get; set; }
        public HistoryDestination Destination { get; set; }
        public string ChannelId { get; set; }
        public DateTime Queued { get; set; }
        public DateTime? Posted { get; set; }
        public string MessageId { get; set; }
    }

    public interface IHistoryPostRepository : IBasicRepository<HistoryPost>
    {
        List<HistoryPost> Waiting();

        // The request IDs that already have a post queued or done in the destination's channel.
        IReadOnlySet<int> Known(HistoryDestination destination, string channelId);
    }

    public class HistoryPostRepository : BasicRepository<HistoryPost>, IHistoryPostRepository
    {
        public HistoryPostRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<HistoryPost> Waiting() => Query(p => p.Posted == null).ToList();

        public IReadOnlySet<int> Known(HistoryDestination destination, string channelId) =>
            Query(p => p.Destination == destination && p.ChannelId == channelId).Select(p => p.RequestId).ToHashSet();
    }
}
