using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // Which seasons a TV request asks for: "All seasons" (sent at once) or "Choose seasons" (a menu, then a plain-words
    // confirmation), never both; the final status always says which seasons were requested. The season menu on cards
    // posted before the two paths existed keeps working.
    [TestFixture]
    public class SeasonChoiceFixture : RequestCardScenario
    {
        [SetUp]
        public async Task AutoApproveTv()
        {
            await SetAccess(Alice, "tv", "auto");
        }

        [Test]
        public async Task details_offer_two_season_paths_and_no_mixed_menu()
        {
            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{DarkMatter}"))).LastReply;

            card.Buttons.Select(b => (b.Label, b.CustomId)).Should().Equal(
                (DiscordText.AllSeasons, $"req-tv-all:{DarkMatter}"),
                (DiscordText.ChooseSeasonsLabel, $"req-tv-choose:{DarkMatter}"));
            card.SelectMenu.Should().BeNull();
        }

        [Test]
        public async Task all_seasons_sends_at_once_with_every_regular_season_and_new_ones_but_not_specials()
        {
            var final = (await Click(Alice, $"req-tv-all:{DarkMatter}")).LastReply;

            final.Content.Should().StartWith(DiscordText.FinalSent).And.NotContain("You chose");
            final.Content.Should().Contain("All seasons (1–2) and new seasons as they air, without specials.");
            final.Buttons.Select(b => b.CustomId).Should().Equal("req-add:1"); // once sent, seasons can only be added
            var added = Sonarr.Adds.Single();
            added.Seasons.Should().Equal(1, 2);
            added.MonitorFuture.Should().BeTrue();
            added.Item.HasSpecials.Should().BeTrue();
        }

        [Test]
        public async Task choose_seasons_lists_specials_and_each_season_but_never_all()
        {
            var step = (await Click(Alice, $"req-tv-choose:{DarkMatter}")).LastReply;

            step.SelectMenu.CustomId.Should().Be($"req-tv-pick:{DarkMatter}:0:");
            step.SelectMenu.Options.Select(o => o.Value).Should().Equal("s0", "s1", "s2");
            step.SelectMenu.Options.Select(o => o.Label).Should().NotContain(DiscordText.AllSeasons);
            step.SelectMenu.MaxValues.Should().Be(3);
            step.Buttons.Select(b => b.CustomId).Should().Equal($"req-tv-page:{DarkMatter}:0:f", $"req-tv-review:{DarkMatter}:", $"req-tv-back:{DarkMatter}");
        }

        [Test]
        public async Task one_chosen_season_is_only_that_season()
        {
            var confirm = (await Choose(Alice, $"req-tv-pick:{DarkMatter}", "s2")).LastReply;

            confirm.Content.Should().Contain("You chose **season 2 only; new seasons won't be added**.");
            confirm.Buttons.First().CustomId.Should().Be($"req-tv-ok:{DarkMatter}:2");

            var final = (await Click(Alice, confirm.Buttons.First().CustomId)).LastReply;

            Sonarr.Adds.Single().Seasons.Should().Equal(2);
            Sonarr.Adds.Single().MonitorFuture.Should().BeFalse();
            final.Content.Should().Contain("Season 2 only; new seasons won't be added.");
        }

        [Test]
        public async Task specials_are_only_included_when_picked_and_future_is_optional()
        {
            var confirm = (await Choose(Alice, $"req-tv-pick:{DarkMatter}", "s0", "s1", "future")).LastReply;

            confirm.Content.Should().Contain("You chose **specials and season 1, plus new seasons as they air**.");
            confirm.Buttons.First().CustomId.Should().Be($"req-tv-ok:{DarkMatter}:0-1f");

            await Click(Alice, confirm.Buttons.First().CustomId);

            Sonarr.Adds.Single().Seasons.Should().Equal(0, 1);
            Sonarr.Adds.Single().MonitorFuture.Should().BeTrue();
            LastRequest.Seasons.Should().Equal(0, 1);
        }

        [Test]
        public async Task only_new_seasons_is_a_valid_choice()
        {
            var confirm = (await Choose(Alice, $"req-tv-pick:{DarkMatter}", "future")).LastReply;

            confirm.Content.Should().Contain("only new seasons as they air (no existing seasons)");
            await Click(Alice, confirm.Buttons.First().CustomId);

            Sonarr.Adds.Single().Seasons.Should().BeEmpty();
            Sonarr.Adds.Single().MonitorFuture.Should().BeTrue();
        }

        [Test]
        public async Task back_returns_to_the_details()
        {
            var card = (await Click(Alice, $"req-tv-back:{DarkMatter}")).LastReply;

            card.Buttons.Select(b => b.CustomId).Should().Equal($"req-tv-all:{DarkMatter}", $"req-tv-choose:{DarkMatter}");
        }

        [Test]
        public async Task a_tampered_season_choice_is_refused()
        {
            (await Click(Alice, $"req-tv-ok:{DarkMatter}:junk")).LastReply.Content.Should().Be(DiscordText.ChooseSeasons);
            RequestStore.All().Should().BeEmpty();
        }

        // "all" with a season (possible only on the old menu) means all, and says so.
        [TestCase("s2", "2", false, "season 2 only; new seasons won't be added")]
        [TestCase("all,future", "1,2", true, "all seasons (1–2) and new seasons as they air, without specials")]
        [TestCase("future", "", true, "only new seasons as they air (no existing seasons)")]
        [TestCase("all,s2", "1,2", false, "seasons 1–2 only; new seasons won't be added")]
        public async Task the_old_season_menu_requests_and_monitors_what_was_picked(string picks, string seasons, bool future, string said)
        {
            var expected = seasons.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

            var final = (await RequestTv(Alice, Severance, picks.Split(','))).LastReply;

            LastRequest.Seasons.Should().Equal(expected);
            LastRequest.MonitorFutureSeasons.Should().Be(future);
            Sonarr.Adds.Single().Seasons.Should().Equal(expected);
            Sonarr.Adds.Single().MonitorFuture.Should().Be(future);
            final.Content.Should().Contain(DiscordText.SeasonsLine(said));
        }

        [Test]
        public async Task an_old_menu_pick_of_a_season_the_show_lacks_asks_to_choose_again()
        {
            (await RequestTv(Alice, Severance, "s9")).LastReply.Content.Should().Contain(DiscordText.ChooseSeasons);
            RequestStore.All().Should().BeEmpty();
        }

        [Test]
        public void season_values_parse()
        {
            var choice = RequestCards.ParseSeasons(new[] { "s1", "s10", "future", "junk" });

            choice.All.Should().BeFalse();
            choice.Seasons.Should().Equal(1, 10);
            choice.Future.Should().BeTrue();
        }

        [TestCase("all")]
        [TestCase("2")]
        [TestCase("0-1f")]
        [TestCase("1-3.5.7-8f")]
        [TestCase("f")]
        public void season_choices_round_trip_through_the_button(string spec)
        {
            RequestCards.EncodeSeasons(RequestCards.DecodeSeasons(spec)).Should().Be(spec);
        }
    }
}
