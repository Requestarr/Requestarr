using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Admin
{
    // A type whose instance for all other requests was turned off, deleted or given another purpose while others of it
    // are still in use: those requests are paused (nothing is handed over by itself), and this waits on the admin until
    // one is set to take them. Key "n2": content type 2, TV. Since: when it started, kept across restarts.
    public class NoAllOtherSource : IAdminQueueSource, IHandle<BackendsCheckedEvent>
    {
        private static readonly ContentType[] Types = { ContentType.Tv, ContentType.Movie };

        private readonly IBackendService _backends;
        private readonly IConfigService _config;
        private readonly IEventAggregator _events;

        public NoAllOtherSource(IBackendService backends, IConfigService config, IEventAggregator events)
        {
            _backends = backends;
            _config = config;
            _events = events;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public static string Key(ContentType type) => $"n{(int)type}";

        public IEnumerable<QueueItem> Items() => Types.Where(Missing).Select(type => new QueueItem(Key(type), Since(type) ?? Mark(type))).ToList();

        // After every save, delete and connection check: the item comes and goes with the instances, and the admin
        // panel's count follows.
        public void Handle(BackendsCheckedEvent message)
        {
            var changed = false;

            foreach (var type in Types.Where(type => Missing(type) != Since(type).HasValue))
            {
                Write(type, Missing(type) ? Clock() : null);
                changed = true;
            }

            if (changed)
            {
                _events.PublishEvent(new AdminQueueChangedEvent());
            }
        }

        // Some instance of the type is in use, but none takes its other requests.
        private bool Missing(ContentType type) => _backends.AllOther(type) == null && _backends.Default(type) != null;

        private DateTime Mark(ContentType type)
        {
            var now = Clock();
            Write(type, now);

            return now;
        }

        private DateTime? Since(ContentType type)
        {
            var stored = type == ContentType.Movie ? _config.MovieAllOtherMissingSince : _config.TvAllOtherMissingSince;

            return string.IsNullOrEmpty(stored) ? null : DateTime.Parse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
        }

        private void Write(ContentType type, DateTime? since)
        {
            var value = since?.ToString("o", CultureInfo.InvariantCulture);

            if (type == ContentType.Movie)
            {
                _config.MovieAllOtherMissingSince = value;
            }
            else
            {
                _config.TvAllOtherMissingSince = value;
            }
        }
    }
}
