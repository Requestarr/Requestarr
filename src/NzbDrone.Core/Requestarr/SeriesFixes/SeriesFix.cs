using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.SeriesFixes
{
    // A ready fix for a show Sonarr grabs wrong (a name clash pulls in another series' releases): release terms for that
    // series alone, found by its TheTVDB ID, never its name. Nothing is built in Sonarr until the show is requested
    // (SeriesFixService). Shipped fixes come with Requestarr and can be changed or turned off, never deleted, so an
    // update doesn't bring a deleted one back.
    public class SeriesFix : ModelBase
    {
        public int TvdbId { get; set; }

        // The show as admins know it, for the list and the Sonarr profile's name ("Requestarr fix: Pokémon (1997)").
        public string Series { get; set; }

        // The short name in the Sonarr tag: requestarr-fix-<ShortName>.
        public string ShortName { get; set; }
        public List<string> MustContain { get; set; } = new();
        public List<string> MustNotContain { get; set; } = new();

        // Why, in plain words: shown on the request card when the fix is applied.
        public string Reason { get; set; }
        public bool Enabled { get; set; } = true;
        public bool Shipped { get; set; }

        public string TagName => $"{SeriesFixNames.TagPrefix}{ShortName}";
        public string ProfileName => $"{SeriesFixNames.ProfilePrefix}{Series}";
    }

    public static class SeriesFixNames
    {
        // Every name starts with "requestarr", as the anime tags and profiles do, so it's never mistaken for the owner's.
        public const string TagPrefix = "requestarr-fix-";
        public const string ProfilePrefix = "Requestarr fix: ";
    }

    public interface ISeriesFixRepository : IBasicRepository<SeriesFix>
    {
        SeriesFix ForSeries(int tvdbId);
    }

    public class SeriesFixRepository : BasicRepository<SeriesFix>, ISeriesFixRepository
    {
        public SeriesFixRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public SeriesFix ForSeries(int tvdbId) => Query(f => f.TvdbId == tvdbId).FirstOrDefault();
    }
}
