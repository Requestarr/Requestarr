using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Requests
{
    // Finding a title to request: /request {type} {query}, its type suggested from the caller's own access, a search by
    // title or ID through the assigned instance's own lookup, and the result it opens. Whatever was typed is checked
    // again on the server.
    [TestFixture]
    public class RequestSearchFixture : RequestScenario
    {
        private async Task<string[]> Suggest(ulong user, string typed = "", ulong? channel = null)
        {
            var run = await Harness.SendAsync(FakeInteraction.Autocomplete("request", "type", typed).By(user).InGuild(FakeDiscordGuild.Guild).InChannel(channel ?? ChannelOf(user)));
            run.Responder.Responses.Single().Kind.Should().Be(ResponseKind.Suggestions);
            return run.Responder.Suggestions.Select(s => s.Value).ToArray();
        }

        [Test]
        public async Task autocomplete_offers_only_types_the_user_can_request()
        {
            (await Suggest(Alice)).Should().Equal("movie", "tv");

            await SetAccess(Alice, "tv", "off");
            (await Suggest(Alice)).Should().Equal("movie");

            await SetAccess(Alice, "movie", "off");
            (await Suggest(Alice)).Should().BeEmpty();
        }

        [Test]
        public async Task autocomplete_never_offers_music_comics_or_books()
        {
            await SetAccess(Alice, "music", "auto");

            (await Suggest(Alice)).Should().NotContain("music");
        }

        [TestCase("mo", "movie")]
        [TestCase("tv", "tv")]
        public async Task autocomplete_filters_by_what_was_typed(string typed, string offered)
        {
            (await Suggest(Alice, typed)).Should().Equal(offered);
        }

        [Test]
        public async Task autocomplete_offers_nothing_in_the_admin_channel()
        {
            var run = await Harness.SendAsync(FakeInteraction.Autocomplete("request", "type", "").By(FakeDiscordGuild.Owner).InGuild(FakeDiscordGuild.Guild).InChannel(AdminChannel));

            run.Responder.Suggestions.Should().BeEmpty("admins request from their own channel");
        }

        [Test]
        public async Task autocomplete_offers_nothing_to_people_who_cannot_request_here()
        {
            (await Suggest(Alice, channel: ChannelOf(Bob))).Should().BeEmpty();
            (await Suggest(Moderator, channel: ChannelOf(Alice))).Should().BeEmpty();
        }

        [Test]
        public async Task movie_search_should_list_results_in_a_select_menu()
        {
            var run = await Harness.SendAsync(Search(Alice, "movie", "dune"));

            run.LastReply.Content.Should().Contain(DiscordText.SearchResults(ContentType.Movie, "dune", 2));
            var menu = run.LastReply.SelectMenu;
            menu.CustomId.Should().Be("req-pick:movie:dune");
            menu.Options.Select(o => (o.Label, o.Value)).Should().Equal(("Dune (2021)", Dune.ToString()), ("Dune: Part Two (2024)", DuneTwo.ToString()));
        }

        [Test]
        public async Task results_should_respect_discord_limits()
        {
            for (var i = 0; i < 40; i++)
            {
                Radarr.Catalog.Add(new MediaLookupResult { Title = "Long " + new string('x', 150) + i, TmdbId = 1000 + i, Overview = new string('o', 300) });
            }

            var run = await Harness.SendAsync(Search(Alice, "movie", "long"));

            var menu = run.LastReply.SelectMenu;
            menu.Options.Count.Should().Be(RequestService.MaxSearchResults);
            menu.Options.Should().OnlyContain(o => o.Label.Length <= DiscordLimits.OptionLabelLength && o.Description.Length <= DiscordLimits.OptionLabelLength);
        }

        [Test]
        public async Task picking_a_result_should_show_detail_card_with_poster()
        {
            var run = await Harness.SendAsync(FakeInteraction.SelectMenu("req-pick:movie", Dune.ToString()).By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            var card = run.LastReply;
            card.Embeds.Single().Title.Should().Be("Dune (2021)");
            card.Embeds.Single().Description.Should().Be("Paul Atreides...");
            card.Embeds.Single().ImageUrl.Should().Be("https://image.example/dune.jpg");
            card.Buttons.Single().CustomId.Should().Be($"req-make:movie:{Dune}");
        }

        [Test]
        public async Task request_by_tmdb_and_tvdb_id_should_show_detail_directly()
        {
            (await Harness.SendAsync(Search(Alice, "movie", $"tmdb:{Dune}"))).LastReply.Embeds.Single().Title.Should().Be("Dune (2021)");
            (await Harness.SendAsync(Search(Alice, "tv", $"TVDB: {Severance}"))).LastReply.Embeds.Single().Title.Should().Be("Severance (2022)");
        }

        [Test]
        public async Task an_unknown_id_should_explain()
        {
            (await Harness.SendAsync(Search(Alice, "movie", "tmdb:999"))).LastReply.Content.Should().Contain(DiscordText.TitleNotFound);
        }

        [Test]
        public async Task id_of_the_wrong_kind_is_explained()
        {
            (await Harness.SendAsync(Search(Alice, "tv", $"tmdb:{Dune}"))).LastReply.Content.Should().Contain(DiscordText.TmdbIsForMovies);
            (await Harness.SendAsync(Search(Alice, "movie", $"tvdb:{Severance}"))).LastReply.Content.Should().Contain(DiscordText.TvdbIsForTv);
        }

        [Test]
        public async Task unreachable_backend_should_be_reported()
        {
            Radarr.Unreachable = true;

            (await Harness.SendAsync(Search(Alice, "movie", "dune"))).LastReply.Content.Should().Contain(DiscordText.RequestsPaused(ContentType.Movie));

            ExceptionVerification.IgnoreWarns();
        }

        [Test]
        public async Task searching_in_someone_elses_channel_should_be_refused()
        {
            var run = await Harness.SendAsync(FakeInteraction.SlashCommand("request").WithOption("type", "movie").WithOption("query", "dune").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Bob)));

            run.LastReply.Content.Should().Contain(DiscordText.OwnChannelOnly);
        }

        [Test]
        public async Task typed_types_are_rechecked_server_side()
        {
            await SetAccess(Alice, "movie", "off");

            (await Harness.SendAsync(Search(Alice, "movie", "dune"))).LastReply.Content.Should().Contain(DiscordText.AccessOff(ContentType.Movie));
            (await Harness.SendAsync(Search(Alice, "music", "x"))).LastReply.Content.Should().Contain(DiscordText.MusicNotYet);
            (await Harness.SendAsync(Search(Alice, "books", "x"))).LastReply.Content.Should().Contain(DiscordText.ChooseType);
        }

        // The service refuses music on its own too, for callers that don't come through the Discord command.
        [Test]
        public async Task music_is_not_requestable_yet()
        {
            var caller = new RequestCaller(UserFor(Alice), false, Actor.System, "alice");

            (await Requests.SearchAsync(caller, ContentType.Music, "x")).Error.Should().Be(DiscordText.MusicNotYet);
        }

        [Test]
        public async Task admin_search_in_admin_channel_points_to_their_own_channel()
        {
            var run = await Harness.SendAsync(AdminRequest("movie", "dune"));

            run.LastReply.Content.Should().Be(DiscordText.AdminRequestsInOwnChannel(null));
            run.LastReply.SelectMenu.Should().BeNull("the admin channel is for admin actions");
        }
    }
}
