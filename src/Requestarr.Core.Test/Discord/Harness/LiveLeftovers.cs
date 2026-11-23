using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Core.Test.Discord.Harness
{
    // A live check leaves nothing behind in the dev server: the bot's messages it posted in a channel (made-up versions,
    // fixture items, their cards) are removed when it ends. The dev stack's bot posts its own again when it starts.
    public sealed class LiveLeftovers
    {
        private const int MessagesRead = 500;

        private readonly IDiscordGuildGateway _gateway;
        private readonly Dictionary<ulong, HashSet<ulong>> _before = new();

        public LiveLeftovers(IDiscordGuildGateway gateway)
        {
            _gateway = gateway;
        }

        // What was there before the check: kept.
        public async Task NoteAsync(params ulong[] channels)
        {
            foreach (var channel in channels)
            {
                _before[channel] = (await _gateway.GetOwnMessagesAsync(channel, MessagesRead)).Select(m => m.Id).ToHashSet();
            }
        }

        // Removes the rest, and says how many went and how many of the check's are still there (none).
        public async Task<(int Removed, int Left)> RemoveAsync()
        {
            var removed = 0;

            foreach (var (channel, kept) in _before)
            {
                foreach (var message in (await _gateway.GetOwnMessagesAsync(channel, MessagesRead)).Where(m => !kept.Contains(m.Id)))
                {
                    await _gateway.DeleteMessageAsync(channel, message.Id);
                    removed++;
                }
            }

            var left = 0;

            foreach (var (channel, kept) in _before)
            {
                left += (await _gateway.GetOwnMessagesAsync(channel, MessagesRead)).Count(m => !kept.Contains(m.Id));
            }

            return (removed, left);
        }
    }
}
