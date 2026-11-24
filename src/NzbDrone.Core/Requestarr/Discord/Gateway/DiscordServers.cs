using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NzbDrone.Common.Serializer;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Discord.Gateway
{
    public interface IDiscordServers
    {
        // Every server the bot is in, Requestarr's first, then those it was in and isn't any more: their cards stay, so the
        // admin can add it back.
        IReadOnlyList<DiscordServer> All();

        // The bot leaves a server it only reads, or Requestarr forgets one it isn't in any more; never Requestarr's own.
        // False when it knows no such server.
        Task<bool> RemoveAsync(ulong guildId);
    }

    // The servers the bot has been in, by name and picture: Discord only says which it's in now, so one that removed the
    // bot would be gone without a trace.
    public class DiscordServers : IDiscordServers, IHandle<DiscordStatusChangedEvent>
    {
        private readonly IDiscordClientHost _host;
        private readonly IDiscordSettingsStore _settings;
        private readonly IConfigService _config;
        private readonly IEventAggregator _eventAggregator;
        private readonly object _lock = new();

        public DiscordServers(IDiscordClientHost host, IDiscordSettingsStore settings, IConfigService config, IEventAggregator eventAggregator)
        {
            _host = host;
            _settings = settings;
            _config = config;
            _eventAggregator = eventAggregator;
        }

        public IReadOnlyList<DiscordServer> All()
        {
            var known = Known().ToDictionary(s => s.Id);
            var servers = _host.Servers
                .Select(s => s.Name == null && known.TryGetValue(s.Id, out var before) ? s with { Name = before.Name, IconUrl = before.IconUrl } : s)
                .ToList();

            // Before the bot is online it's in no server as far as Discord has said yet.
            if (_host.IsOnline)
            {
                servers.AddRange(known.Values
                    .Where(k => servers.All(s => s.Id != k.Id))
                    .Select(k => new DiscordServer(k.Id, k.Name, k.IconUrl, false, false, false))
                    .ToList());
            }

            return servers;
        }

        public async Task<bool> RemoveAsync(ulong guildId)
        {
            if (guildId == _settings.Current.GuildId)
            {
                return false;
            }

            var left = await _host.LeaveServerAsync(guildId);
            var forgotten = Forget(guildId.ToString(CultureInfo.InvariantCulture));

            if (forgotten && !left)
            {
                _eventAggregator.PublishEvent(new DiscordStatusChangedEvent());
            }

            return left || forgotten;
        }

        public void Handle(DiscordStatusChangedEvent message)
        {
            lock (_lock)
            {
                var known = Known();
                var joined = _host.Servers.Where(s => s.Joined && s.Name != null).Select(s => new KnownServer(s.Id, s.Name, s.IconUrl)).ToList();
                Save(known, known.Where(k => joined.All(j => j.Id != k.Id)).Concat(joined).OrderBy(k => k.Name).ToList());
            }
        }

        private bool Forget(string id)
        {
            lock (_lock)
            {
                var known = Known();

                return Save(known, known.Where(k => k.Id != id).ToList());
            }
        }

        private bool Save(List<KnownServer> before, List<KnownServer> after)
        {
            if (after.SequenceEqual(before))
            {
                return false;
            }

            _config.DiscordKnownServers = STJson.ToJson(after);
            return true;
        }

        private List<KnownServer> Known() =>
            string.IsNullOrEmpty(_config.DiscordKnownServers) ? new List<KnownServer>() : STJson.Deserialize<List<KnownServer>>(_config.DiscordKnownServers);

        private record KnownServer(string Id, string Name, string IconUrl);
    }
}
