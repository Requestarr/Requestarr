using System;
using System.Linq;
using NzbDrone.Core.Datastore;
using Requestarr.Core.Backends;

namespace Requestarr.Core.Requests
{
    // What a request's title is, as Sonarr or Radarr described it when it was looked up, once: the overview, the release
    // dates (movies) or first air date and network (TV), and the title's slug for links to its page there. Checked: when
    // the lookup was made; a title nothing found has only that.
    public class RequestMedia : IEmbeddedDocument
    {
        public string Overview { get; set; }
        public DateTime? InCinemas { get; set; }
        public DateTime? DigitalRelease { get; set; }
        public DateTime? PhysicalRelease { get; set; }
        public DateTime? FirstAired { get; set; }
        public string Network { get; set; }
        public string TitleSlug { get; set; }
        public DateTime Checked { get; set; }

        // The year it first came out (its earliest release or first air date); null when no date is known.
        public int? FirstYear() => new[] { InCinemas, DigitalRelease, PhysicalRelease, FirstAired }.Where(d => d.HasValue).Min()?.Year;

        public static RequestMedia From(MediaLookupResult item, DateTime checkedAt) => new()
        {
            Overview = item?.Overview,
            InCinemas = item?.InCinemas,
            DigitalRelease = item?.DigitalRelease,
            PhysicalRelease = item?.PhysicalRelease,
            FirstAired = item?.FirstAired,
            Network = item?.Network,
            TitleSlug = item?.TitleSlug,
            Checked = checkedAt
        };
    }
}
