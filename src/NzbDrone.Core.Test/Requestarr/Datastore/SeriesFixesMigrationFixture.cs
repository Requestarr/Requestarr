using System.Linq;
using Dapper;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration;
using NzbDrone.Core.Test.Framework;

namespace Requestarr.Core.Test.Datastore
{
    // A new or upgraded install starts with the one proven fix, and requests can record the fix they went in with.
    [TestFixture]
    public class SeriesFixesMigrationFixture : MigrationTest<series_fixes>
    {
        [Test]
        public void ships_only_the_pokemon_fix()
        {
            var db = WithDapperMigrationTestDb(_ => { });

            var fixes = db.Query<(int TvdbId, string Series, string MustNotContain, bool Shipped)>("SELECT \"TvdbId\", \"Series\", \"MustNotContain\", \"Shipped\" FROM \"SeriesFixes\"").ToList();

            fixes.Should().Equal((series_fixes.PokemonTvdbId, "Pokémon (1997)", "[\"Horizons\"]", true));
            db.Query<string>("SELECT name FROM pragma_table_info('Requests')").Should().Contain("AppliedFix");
        }
    }
}
