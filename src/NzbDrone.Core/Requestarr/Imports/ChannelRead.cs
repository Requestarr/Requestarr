using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Requestarr.Core.Imports
{
    // One old channel's history as read for the Import page, kept until Requestarr restarts: picking the channel again,
    // or picking it with others, uses what was read instead of reading it from Discord twice. A preview that starts
    // while it's still being read waits for the same read.
    public sealed class ChannelRead
    {
        private readonly object _lock = new();
        private readonly List<SourceMessage> _messages = new();
        private bool _done;
        private bool _withheld;

        public ChannelRead(ImportChannel channel)
        {
            Channel = channel;
        }

        public ImportChannel Channel { get; }

        // The reading itself, started once; it fails when Discord stops answering part way through.
        public Task Reading { get; set; }

        public IReadOnlyList<SourceMessage> Messages
        {
            get
            {
                lock (_lock)
                {
                    return _messages.ToList();
                }
            }
        }

        // Whether Discord withheld some replies' text: the bot may not read other bots' messages.
        public bool Withheld
        {
            get
            {
                lock (_lock)
                {
                    return _withheld;
                }
            }
        }

        public ImportProgress Progress
        {
            get
            {
                lock (_lock)
                {
                    return new ImportProgress(Channel.Id, Channel.Name, _messages.Count, _done);
                }
            }
        }

        public void Add(IReadOnlyList<SourceMessage> page, bool withheld)
        {
            lock (_lock)
            {
                _messages.AddRange(page);
                _withheld |= withheld;
            }
        }

        public void Finish()
        {
            lock (_lock)
            {
                _done = true;
            }
        }
    }
}
