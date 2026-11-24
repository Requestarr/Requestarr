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
        // False when it knows no such server, or the bot isn't connected.
        Task<bool> RemoveAsync(ulong guildId);
    }

    // The servers the bot has been in, by name and picture: Discord only says which it's in now, so one that removed the
    // bot would be gone without a trace. Kept for the bot of the token in use: another bot was never in them.
    public class DiscordServers : IDiscordServers, IHandle<DiscordStatusChangedEvent>
    {
        private readonly IDiscordClientHost _host;
        private readonly IDiscordSettingsStore _settings;
        private readonly IConfigService _config;
        private readonly IEventAggregator _eventAggregator;
        private readonly object _lock = new();

        // Left on purpose, until Discord says the bot is out: not to be remembered again in between.
        private readonly HashSet<string> _leaving = new();

        private List<KnownServer> _known;

        public DiscordServers(IDiscordClientHost host, IDiscordSettingsStore settings, IConfigService config, IEventAggregator eventAggregator)
        {
            _host = host;
            _settings = settings;
            _config = config;
            _eventAggregator = eventAggregator;
        }

        public IReadOnlyList<DiscordServer> All()
        {
            // Before the bot is online it's in no server as far as Discord has said yet. Asked first: the bot may connect
            // while the list is made.
            var isOnline = _host.IsOnline;
            var known = Known().ToDictionary(s => s.Id);
            var servers = _host.Servers
                .Select(s => s.Name == null && known.TryGetValue(s.Id, out var before) ? s with { Name = before.Name, IconUrl = before.IconUrl } : s)
                .ToList();

            if (!isOnline)
            {
                return servers;
            }

            var ours = Text(_settings.Current.GuildId);
            var left = known.Values
                .Where(k => k.Id != ours && servers.All(s => s.Id != k.Id))
                .Select(k => new DiscordServer(k.Id, k.Name, k.IconUrl, IsOurs: false, Reachable: false, Joined: false));

            return servers.Concat(left).ToList();
        }

        public async Task<bool> RemoveAsync(ulong guildId)
        {
            if (guildId == _settings.Current.GuildId || !_host.IsOnline)
            {
                return false;
            }

            var left = await _host.LeaveServerAsync(guildId);
            var forgotten = Forget(Text(guildId), left);

            if (left || forgotten)
            {
                _eventAggregator.PublishEvent(new DiscordStatusChangedEvent());
            }

            return left || forgotten;
        }

        public void Handle(DiscordStatusChangedEvent message)
        {
            // The bot's application isn't known until its first connection.
            if (_settings.Current.ApplicationId == 0)
            {
                return;
            }

            lock (_lock)
            {
                var servers = _host.Servers;
                _leaving.RemoveWhere(id => servers.All(s => s.Id != id));

                var known = Known();
                var joined = servers
                    .Where(s => s.Joined && s.Name != null && !_leaving.Contains(s.Id))
                    .Select(s => new KnownServer(Text(_settings.Current.ApplicationId), s.Id, s.Name, s.IconUrl))
                    .ToList();
                var left = known.Where(k => joined.All(j => j.Id != k.Id));

                Save(known, left.Concat(joined).OrderBy(k => k.Name).ToList());
            }
        }

        private bool Forget(string id, bool left)
        {
            lock (_lock)
            {
                if (left)
                {
                    _leaving.Add(id);
                }

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

            _known = after;
            _config.DiscordKnownServers = STJson.ToJson(after);
            return true;
        }

        private List<KnownServer> Known()
        {
            var bot = Text(_settings.Current.ApplicationId);

            lock (_lock)
            {
                _known ??= string.IsNullOrEmpty(_config.DiscordKnownServers) ? new List<KnownServer>() : STJson.Deserialize<List<KnownServer>>(_config.DiscordKnownServers);

                return _known.Where(k => k.Bot == bot).ToList();
            }
        }

        private static string Text(ulong id) => id.ToString(CultureInfo.InvariantCulture);

        private record KnownServer(string Bot, string Id, string Name, string IconUrl);
    }
}
