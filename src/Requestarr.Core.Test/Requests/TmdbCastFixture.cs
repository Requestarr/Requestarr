using System;
using System.Buffers.Binary;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Framework;

namespace Requestarr.Core.Test.Requests
{
    // An optional The Movie Database key adds the top cast to result lines and the card, with The Movie Database's
    // approved logo and notice only while a key is set. The images travel with the message, from inside Requestarr.
    [TestFixture]
    public class TmdbCastFixture : RequestCardScenario
    {
        private const string Key = "v3-secret-key-123";

        [Test]
        public async Task without_a_key_lines_show_year_network_and_genre()
        {
            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;

            results.SelectMenu.Options.Select(o => (o.Label, o.Description)).Should().Equal(
                ("Dark Matter (2024)", "2024 · Apple TV · Drama"),
                ("Dark Matter (2015)", "2015 · SYFY · Adventure"));
            results.Content.Should().NotContain("TMDB");
            Tmdb.Calls.Should().Be(0);
        }

        [Test]
        public async Task with_a_key_lines_show_the_top_three_cast()
        {
            TmdbKey.Set(Key);

            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;

            results.SelectMenu.Options.Select(o => o.Description).Should().Equal(
                "2024 · Apple TV · Joel Edgerton, Jennifer Connelly, Alice Braga",
                "2015 · SYFY · Marc Bendavid, Melissa O'Neil, Anthony Lemke");
            results.Content.Should().EndWith("\n-# Cast from TMDB");
        }

        [Test]
        public void lines_stay_within_100_characters_by_dropping_cast_from_the_end()
        {
            var item = new MediaLookupResult
            {
                Year = 2024,
                Network = "A Very Long Network Name Indeed",
                Genres = { "Drama" },
                Cast = { "Christopher Abbott-Longname", "Elizabeth Mary Winstead-Smith", "Bartholomew Cubbins The Third" }
            };

            var line = RequestCards.ResultLine(item);

            line.Length.Should().BeLessThanOrEqualTo(DiscordLimits.OptionLabelLength);
            line.Should().Be("2024 · A Very Long Network Name Indeed · Christopher Abbott-Longname, Elizabeth Mary Winstead-Smith");

            item.Network = new string('N', 120);
            RequestCards.ResultLine(item).Length.Should().BeLessThanOrEqualTo(DiscordLimits.OptionLabelLength);
        }

        [Test]
        public async Task with_a_key_the_card_lists_top_cast_and_episodes_with_the_small_tmdb_logo()
        {
            TmdbKey.Set(Key);

            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;
            var embed = card.Embeds.Single();

            Field(card, "Cast").Should().Be("Joel Edgerton, Jennifer Connelly, Alice Braga");
            Field(card, "Episodes").Should().Be("9");
            embed.Footer.Should().Be(DiscordText.TmdbAttribution);
            embed.FooterIconUrl.Should().Be(DiscordText.TmdbLogoUrl);
            embed.ThumbnailUrl.Should().BeNull("the logo is a footer icon, less prominent than the card");
        }

        [Test]
        public async Task without_a_key_there_is_no_logo_or_credit()
        {
            var embed = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply.Embeds.Single();

            embed.Footer.Should().BeNull();
            embed.FooterIconUrl.Should().BeNull();
        }

        [Test]
        public async Task cards_without_cast_have_no_logo_even_with_a_key()
        {
            TmdbKey.Set(Key);

            var embed = (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{DuneTwo}"))).LastReply.Embeds.Single();

            embed.FooterIconUrl.Should().BeNull();
        }

        [Test]
        public async Task the_movie_database_failing_never_breaks_a_request()
        {
            TmdbKey.Set(Key);
            Tmdb.Fail = true;

            var results = (await Harness.SendAsync(Search(Alice, "tv", "dark matter"))).LastReply;
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;

            results.SelectMenu.Options.First().Description.Should().Be("2024 · Apple TV · Drama");
            Field(card, "Cast").Should().BeNull();
            card.Embeds.Single().Footer.Should().BeNull();
        }

        [Test]
        public async Task cast_is_cached_between_searches()
        {
            TmdbKey.Set(Key);

            await Harness.SendAsync(Search(Alice, "tv", "dark matter"));
            await Harness.SendAsync(Search(Alice, "tv", "dark matter"));
            await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"));

            Tmdb.Calls.Should().Be(2);
        }

        [Test]
        public async Task the_admin_credits_topic_has_the_notice_and_logo()
        {
            TmdbKey.Set(Key);

            var credits = await Harness.SendAsync(FakeInteraction.SelectMenu("help:admin", "credits").By(FakeDiscordGuild.Owner, "owner").InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            credits.LastReply.Content.Should().StartWith("**Credits**").And.Contain(DiscordText.TmdbNotice);
            credits.LastReply.Embeds.Single().Url.Should().Be(DiscordText.TmdbSite);
            credits.LastReply.Embeds.Single().ThumbnailUrl.Should().Be(DiscordText.TmdbLogoUrl);
        }

        [Test]
        public void the_notice_uses_the_wording_from_tmdbs_terms()
        {
            DiscordText.TmdbNotice.Should().Contain("uses TMDB and the TMDB APIs but is not endorsed, certified, or otherwise approved by TMDB");
        }

        // Nothing is loaded from GitHub: the logo and the feed's width spacer are embedded and attached to the message.
        [Test]
        public async Task the_logo_and_the_feed_spacer_are_built_in_and_attached_to_the_message()
        {
            DiscordText.TmdbLogoUrl.Should().Be("attachment://tmdb-logo.png");
            DiscordText.FeedCardWidthUrl.Should().Be("attachment://feed-card-width.png");

            foreach (var name in new[] { DiscordAssets.TmdbLogo, DiscordAssets.FeedCardWidth })
            {
                var png = DiscordAssets.Bytes(name);
                png.Should().NotBeNull($"{name} is an embedded resource");
                png.Take(8).Should().Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, "it is a PNG");
            }

            var spacer = DiscordAssets.Bytes(DiscordAssets.FeedCardWidth);
            (BinaryPrimitives.ReadInt32BigEndian(spacer.AsSpan(16)), BinaryPrimitives.ReadInt32BigEndian(spacer.AsSpan(20))).Should().Be((800, 1));

            TmdbKey.Set(Key);
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;
            DiscordAssets.For(card).Select(f => f.Name).Should().Equal(DiscordAssets.TmdbLogo);
            DiscordAssets.For(new InteractionReply { Content = "plain" }).Should().BeEmpty();
        }

        [Test]
        public void no_message_image_points_at_github()
        {
            var source = string.Join("\n", Directory.GetFiles(RepoFiles.Path("src", "NzbDrone.Core", "Requestarr"), "*.cs", SearchOption.AllDirectories).Select(File.ReadAllText));

            source.Should().NotContain("githubusercontent").And.NotContain("docs/assets");
        }
    }
}
