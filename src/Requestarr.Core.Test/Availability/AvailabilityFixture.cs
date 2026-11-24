using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Availability;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Availability
{
    // Availability: downloading, available (TV: every aired episode of a season has a file), stuck and failed, from the
    // instance's webhook and the backup poll, each change reposted once to the requester and the feed.
    [TestFixture]
    public class AvailabilityFixture : FeedScenario
    {
        private const int MovieId = 101;

        [Test]
        public async Task a_grab_webhook_reposts_downloading_for_the_user_and_the_feed()
        {
            var request = await SentMovie();
            Radarr.Downloading(MovieId);

            (await Webhook("Grab", request)).Should().Be(WebhookOutcome.Accepted);

            Reload(request.Id).Progress.Should().Be(DownloadProgress.Downloading);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalDownloading}");
            ChannelFor(Alice).MessageIds.Last().ToString().Should().Be(Reload(request.Id).MessageId, "the old message was replaced by the new one");
            State(LatestCard).Should().Be(DiscordText.FinalDownloading);
            CardsFor(request.Id).Should().Be(1);
        }

        [Test]
        public async Task the_same_state_again_reposts_nothing()
        {
            var request = await SentMovie();
            Radarr.Downloading(MovieId);
            await Webhook("Grab", request);
            var userMessages = ChannelFor(Alice).MessageIds.ToList();
            var feedMessages = Feed.MessageIds.ToList();

            await Webhook("Grab", request);
            Radarr.Downloading(MovieId, left: 200);
            await Availability.CheckAllAsync();

            ChannelFor(Alice).MessageIds.Should().Equal(userMessages);
            Feed.MessageIds.Should().Equal(feedMessages);
        }

        [Test]
        public async Task an_import_webhook_makes_it_available()
        {
            var request = await SentMovie();
            Radarr.Imported(MovieId);

            await Webhook("Download", request);

            var done = Reload(request.Id);
            done.Status.Should().Be(RequestStatus.Available);
            done.Progress.Should().Be(DownloadProgress.Imported);
            done.Fulfilled.Should().Be(Now);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalReady}").And.Contain("ready to watch");
            Latest(Alice).Buttons.Select(b => b.CustomId).Should().Equal(new[] { $"rm-user:{request.Id}" }, "once it's in the library, its requester can ask to remove it");
            State(CardOf(request.Id)).Should().Be(DiscordText.FinalReady);
            CardOf(request.Id).Buttons.Select(b => b.CustomId).Should().Equal(new[] { $"rm-feed:{request.Id}" }, "the feed card's Delete starts a removal");
            Audits("request.fulfilled").Should().Be(1);
        }

        [Test]
        public async Task the_poll_finds_it_without_any_webhook()
        {
            var request = await SentMovie();
            Radarr.Imported(MovieId);

            await Availability.CheckAllAsync();

            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
            Latest(Alice).Content.Should().Contain(DiscordText.FinalReady);
        }

        [Test]
        public async Task webhook_and_poll_together_tell_everyone_once()
        {
            var request = await SentMovie();
            Radarr.Imported(MovieId);
            var before = ChannelFor(Alice).Messages.Count;

            await Task.WhenAll(Webhook("Download", request), Availability.CheckAllAsync(), Webhook("Download", request));

            Audits("request.fulfilled").Should().Be(1);
            ChannelFor(Alice).Messages.Count.Should().Be(before, "one repost: the old message deleted, one new one posted");
            ChannelFor(Alice).Messages.Count(m => m.Content.Contains(DiscordText.FinalReady)).Should().Be(1);
            CardsFor(request.Id).Should().Be(1);
        }

        [Test]
        public async Task the_poll_waits_for_its_interval()
        {
            var request = await SentMovie();
            await Availability.CheckAllAsync();
            Radarr.Imported(MovieId);

            Now = Now.AddMinutes(AvailabilitySettingsStore.DefaultPollMinutes - 1);
            Availability.Execute(new AvailabilityCheckCommand());
            Reload(request.Id).Status.Should().Be(RequestStatus.Approved);

            Now = Now.AddMinutes(2);
            Availability.Execute(new AvailabilityCheckCommand());
            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
        }

        [Test]
        public async Task a_season_is_available_when_every_aired_episode_has_a_file()
        {
            var request = await SentShow(Alice, DarkMatter2015, 1, 2, 3);
            var series = request.ExternalItemId.Value;
            Sonarr.Season(series, 1, aired: 13, withFiles: 13);
            Sonarr.Season(series, 2, aired: 13, withFiles: 4);
            Sonarr.Season(series, 3, aired: 0, withFiles: 0, upcoming: 10);
            Sonarr.Downloading(series, season: 2);

            await Availability.CheckAllAsync();

            var partly = Reload(request.Id);
            partly.Status.Should().Be(RequestStatus.Approved);
            partly.AvailableSeasons.Should().Equal(1);
            Latest(Alice).Content.Should().Contain(DiscordText.AvailableSoFar(partly)).And.Contain("season 1");
            CardOf(request.Id).Embeds[0].Description.Should().Contain("season 1");

            // Season 2 complete; season 3 hasn't aired, so it doesn't hold the request back.
            Sonarr.Season(series, 2, aired: 13, withFiles: 13);
            Sonarr.Queues.Remove(series);
            await Availability.CheckAllAsync();

            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
            Reload(request.Id).AvailableSeasons.Should().Equal(1, 2);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalReady}").And.Contain("seasons 1–2");
        }

        [Test]
        public async Task an_airing_season_counts_once_every_aired_episode_is_there()
        {
            var request = await SentShow(Alice, Severance, 2);
            Sonarr.Season(request.ExternalItemId.Value, 2, aired: 4, withFiles: 4, upcoming: 6);

            await Availability.CheckAllAsync();

            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
        }

        // A download the instance flags, or an import it blocks, is stuck with the instance's reason.
        [TestCase("warning", "downloading", "The download is stalled with no connections")]
        [TestCase("completed", "importBlocked", "No files found are eligible for import")]
        public async Task a_download_the_instance_flags_is_stuck_with_its_reason(string status, string state, string reason)
        {
            var request = await SentMovie();
            Radarr.QueueIssue(MovieId, status, "warning", state, reason);

            await Availability.CheckAllAsync();

            var stuck = Reload(request.Id);
            stuck.Progress.Should().Be(DownloadProgress.Stalled);
            stuck.ProgressDetail.Should().Be(reason);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalStalled}").And.Contain(reason);
            State(CardOf(request.Id)).Should().Be($"{DiscordText.FinalStalled}: {reason}");
        }

        [Test]
        public async Task a_download_that_stops_moving_for_six_hours_is_stuck()
        {
            var request = await SentMovie();
            Radarr.Downloading(MovieId, left: 600);
            await Availability.CheckAllAsync();

            Now = Now.AddHours(5);
            await Availability.CheckAllAsync();
            Reload(request.Id).Progress.Should().Be(DownloadProgress.Downloading);

            Now = Now.AddHours(2);
            await Availability.CheckAllAsync();
            Reload(request.Id).Progress.Should().Be(DownloadProgress.Stalled);
            Reload(request.Id).ProgressDetail.Should().Be(DiscordText.NoProgressFor(6));

            // It moves again: downloading.
            Radarr.Downloading(MovieId, left: 100);
            await Availability.CheckAllAsync();
            Reload(request.Id).Progress.Should().Be(DownloadProgress.Downloading);
        }

        [Test]
        public async Task a_failed_download_says_so_honestly()
        {
            var request = await SentMovie();
            Radarr.QueueIssue(MovieId, "failed", "error", "failedPending", "Unpacking failed");

            await Availability.CheckAllAsync();

            Reload(request.Id).Progress.Should().Be(DownloadProgress.Failed);
            Latest(Alice).Content.Should().StartWith($"<@{Alice}> {DiscordText.FinalDownloadFailed}").And.Contain("Unpacking failed").And.Contain("Another release is usually tried");
            Reload(request.Id).Status.Should().Be(RequestStatus.Approved, "Radarr keeps trying; the request stays open");
        }

        [Test]
        public async Task followers_hear_about_progress_too()
        {
            var request = await SentMovie();
            await Ask(Bob, "movie", $"tmdb:{Dune}");
            await ClickOn(Bob, ChannelFor(Bob).MessageIds.Last(), $"req-follow:movie:{Dune}");
            Radarr.Imported(MovieId);

            await Webhook("Download", request);

            Latest(Bob).Content.Should().StartWith($"<@{Bob}> {DiscordText.FinalReady}").And.Contain("You're following alice's request");
        }

        [Test]
        public async Task without_discord_the_poll_waits_and_catches_up_later()
        {
            var request = await SentMovie();
            Radarr.Imported(MovieId);
            Discord.IsConnected = false;

            await Availability.CheckAllAsync();
            Reload(request.Id).Status.Should().Be(RequestStatus.Approved, "nobody could be told, so nothing is marked yet");

            Discord.IsConnected = true;
            await Availability.CheckAllAsync();
            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
        }
    }
}
