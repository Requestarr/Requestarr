using FluentMigrator;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: the ready list of series fixes (SeriesFixes), built in Sonarr only when a listed show is requested, and
    // which fix a request was added with (Requests.AppliedFix: its reason, for the request card). Ships with the one
    // proven fix: Pokémon (1997), TheTVDB 76703, never grabbing Pokémon Horizons.
    [Migration(24)]
    public class series_fixes : NzbDroneMigrationBase
    {
        public const int PokemonTvdbId = 76703;

        protected override void MainDbUpgrade()
        {
            Create.TableForModel("SeriesFixes")
                .WithColumn("TvdbId").AsInt32().Unique()
                .WithColumn("Series").AsString()
                .WithColumn("ShortName").AsString()
                .WithColumn("MustContain").AsString().WithDefaultValue("[]")
                .WithColumn("MustNotContain").AsString().WithDefaultValue("[]")
                .WithColumn("Reason").AsString().Nullable()
                .WithColumn("Enabled").AsBoolean().WithDefaultValue(true)
                .WithColumn("Shipped").AsBoolean().WithDefaultValue(false);

            Insert.IntoTable("SeriesFixes").Row(new
            {
                TvdbId = PokemonTvdbId,
                Series = "Pokémon (1997)",
                ShortName = "pokemon",
                MustContain = "[]",
                MustNotContain = "[\"Horizons\"]",
                Reason = "keeps Pokémon Horizons' episodes out of the original series",
                Enabled = true,
                Shipped = true
            });

            Alter.Table("Requests").AddColumn("AppliedFix").AsString().Nullable();
        }
    }
}
