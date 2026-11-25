using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Imports;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Imports
{
    // What was already so when a request was imported is recorded without telling anyone; admins get one post for what
    // needs attention, and only later changes notify, members only once they're onboarded.
    [TestFixture]
    public class ImportAlertsFixture : FeedScenario
    {
        private const ulong Stranger = 900000000000200099;
        private const int StuckMovie = 901;
        private const int OtherMovie = 902;
        private const string ManualImport = "Found matching movie via grab history, but release was matched to movie by ID. Manual Import required.";

        [SetUp]
        public void Portal() =>
            Mocker.GetMock<IPortalLinks>().Setup(p => p.Request(It.IsAny<int>())).Returns((int id) => $"https://requests.example/requests/{id}");

        private Request Imported(int ownerUserId, string title, int item, bool isSummaryDue = true) => RequestStore.Insert(new Request
        {
            OwnerUserId = ownerUserId,
            ContentType = ContentType.Movie,
            Title = title,
            Year = 2017,
            TmdbId = item,
            Status = RequestStatus.Approved,
            BackendId = Mocker.Resolve<IBackendRepository>().All().First(b => b.Implementation == BackendImplementations.Radarr).Id,
            ExternalItemId = item,
            Requested = Now.AddYears(-1),
            Imported = true,
            FirstCheckDue = true,
            SummaryDue = isSummaryDue
        });

        private int StrangerId() => Mocker.Resolve<IUserManagementService>().EnsureImported(Stranger, "Stranger", null, "#old in Old").Id;

        private Dictionary<ulong, int> MessagesByChannel() => Discord.Channels.ToDictionary(c => c.Key, c => c.Value.Messages.Count);

        private void NothingPostedSince(Dictionary<ulong, int> before) => MessagesByChannel().Should().Equal(before);

        [Test]
        public async Task what_was_already_so_at_import_is_recorded_and_tells_nobody()
        {
            var stuck = Imported(UserFor(Alice).Id, "Surf's Up 2: WaveMania", StuckMovie, isSummaryDue: false);
            var there = Imported(UserFor(Bob).Id, "The Silence of the Lambs", OtherMovie, isSummaryDue: false);
            Radarr.QueueIssue(StuckMovie, "completed", "warning", "importBlocked", ManualImport);
            Radarr.Imported(OtherMovie);
            var before = MessagesByChannel();

            await Webhook("ManualInteractionRequired", stuck);
            await Availability.CheckAllAsync();

            NothingPostedSince(before);
            (Reload(stuck.Id).Progress, Reload(stuck.Id).ProgressDetail, Reload(stuck.Id).FirstCheckDue).Should().Be((DownloadProgress.Stalled, ManualImport, false));
            Reload(there.Id).Status.Should().Be(RequestStatus.Available);
            Audits("request.progress").Should().Be(1, "its history still says so");
        }

        [Test]
        public async Task one_silent_post_names_what_needs_attention_once()
        {
            var alices = Imported(UserFor(Alice).Id, "Surf's Up 2: WaveMania", StuckMovie);
            var strangers = Imported(StrangerId(), "The Silence of the Lambs", OtherMovie);
            Radarr.QueueIssue(StuckMovie, "completed", "warning", "importBlocked", ManualImport);
            Radarr.QueueIssue(OtherMovie, "completed", "warning", "importBlocked", ManualImport);
            var name = Mocker.Resolve<IBackendService>().Find(alices.BackendId.Value).Name;
            var before = Feed.Messages.Count;

            await Availability.CheckAllAsync();
            await Availability.CheckAllAsync();

            Feed.Messages.Count.Should().Be(before + 1);
            LatestCard.Quiet.Should().BeTrue();
            LatestCard.Buttons.Should().BeEmpty();
            LatestCard.Embeds.Single().Title.Should().Be(DiscordText.ImportSummaryTitle);
            LatestCard.Embeds.Single().Description.Should().Be(
                $"2 downloads are stuck in {name} and need a manual import: [Surf's Up 2: WaveMania (2017)](https://requests.example/requests/{alices.Id}), " +
                $"[The Silence of the Lambs (2017)](https://requests.example/requests/{strangers.Id}).");
        }

        [Test]
        public async Task nothing_needing_attention_posts_nothing()
        {
            var request = Imported(UserFor(Alice).Id, "Surf's Up 2: WaveMania", StuckMovie);
            Radarr.Downloading(StuckMovie);
            var before = MessagesByChannel();

            await Availability.CheckAllAsync();

            NothingPostedSince(before);
            Reload(request.Id).SummaryDue.Should().BeFalse();
        }

        [Test]
        public async Task a_request_whose_app_is_down_is_first_checked_when_its_back()
        {
            var request = Imported(UserFor(Alice).Id, "Surf's Up 2: WaveMania", StuckMovie);
            Radarr.QueueIssue(StuckMovie, "downloading", "warning", "downloading", "stalled with no connections");
            Radarr.Unreachable = true;
            await Availability.CheckAllAsync();
            Reload(request.Id).FirstCheckDue.Should().BeTrue();
            var before = Feed.Messages.Count;

            Radarr.Unreachable = false;
            await Availability.CheckAllAsync();

            Feed.Messages.Count.Should().Be(before + 1);
            LatestCard.Embeds.Single().Description.Should().StartWith("1 download is stuck in ");
        }

        [Test]
        public async Task a_summary_that_couldnt_be_posted_is_posted_after_the_next_check()
        {
            var request = Imported(UserFor(Alice).Id, "Surf's Up 2: WaveMania", StuckMovie);
            Radarr.QueueIssue(StuckMovie, "failed", "error", "failed", "unpacking failed");
            var feed = new Mock<IRequestFeed>();
            feed.Setup(f => f.PostImportSummaryAsync(It.IsAny<IReadOnlyCollection<Request>>())).ThrowsAsync(new InvalidOperationException("Discord is down"));
            var summary = new ImportSummary(RequestStore, feed.Object, NLog.LogManager.GetLogger("test"));
            request.FirstCheckDue = false;
            RequestStore.Update(request);

            await summary.PostAsync();
            Reload(request.Id).SummaryDue.Should().BeTrue();
            ExceptionVerification.ExpectedWarns(1);

            await Availability.CheckAllAsync();
            Reload(request.Id).SummaryDue.Should().BeFalse();
        }

        [Test]
        public async Task a_change_after_the_import_notifies_the_admins_and_the_onboarded_member()
        {
            var request = Imported(UserFor(Alice).Id, "Surf's Up 2: WaveMania", StuckMovie);
            Radarr.QueueIssue(StuckMovie, "completed", "warning", "importBlocked", ManualImport);
            await Availability.CheckAllAsync();
            var alices = ChannelFor(Alice).Messages.Count;

            Radarr.Imported(StuckMovie);
            await Webhook("Download", request);

            CardsFor(request.Id).Should().Be(1, "the change reached the admin feed");
            ChannelFor(Alice).Messages.Count.Should().Be(alices + 1);
            Latest(Alice).Content.Should().Contain("ready to watch");
        }

        [Test]
        public async Task someone_not_onboarded_is_never_messaged_and_nobody_gets_theirs_instead()
        {
            var request = Imported(StrangerId(), "The Silence of the Lambs", OtherMovie);
            await Availability.CheckAllAsync();
            var before = MessagesByChannel();

            Radarr.QueueIssue(OtherMovie, "completed", "warning", "importBlocked", ManualImport);
            await Webhook("ManualInteractionRequired", request);

            before[FeedChannel]++;
            MessagesByChannel().Should().Equal(before, "only the admin feed is told");
            State(CardOf(request.Id)).Should().Be($"{DiscordText.FinalStalled}: {ManualImport.TrimEnd('.')}");
        }

        [Test]
        public async Task a_request_made_here_still_notifies_at_its_first_check()
        {
            var request = await SentMovie();
            Radarr.QueueIssue(request.ExternalItemId.Value, "completed", "warning", "importBlocked", ManualImport);

            await Availability.CheckAllAsync();

            Latest(Alice).Content.Should().Contain("Manual Import required. The admin can see this too.").And.NotContain("..");
        }

        [Test]
        public void a_reason_ends_once()
        {
            var request = new Request { Id = 432, Title = "Surf's Up 2: WaveMania", Year = 2017, Status = RequestStatus.Approved, Progress = DownloadProgress.Stalled };

            foreach (var (detail, ending) in new[] { (ManualImport, $": {ManualImport}"), ("no space left", ": no space left."), ("Why?!", ": Why."), (" ... ", ".") })
            {
                request.ProgressDetail = detail;
                DiscordText.RequestProgressNotice(request).Should().Be($"Surf's Up 2: WaveMania (2017) (request #432) stopped downloading{ending}");
            }
        }

        [Test]
        public void a_long_list_is_counted_and_the_kinds_are_told_apart()
        {
            var titles = Enumerable.Range(1, 12).Select(i => $"Film {i}").ToList();

            DiscordText.ImportTroubleLine(ImportTrouble.Stuck, "Films", titles).Should().StartWith("12 downloads are stuck in Films: Film 1, ").And.EndWith("Film 10 and 2 more.");
            DiscordText.ImportTroubleLine(ImportTrouble.Failed, "Films", titles.Take(1).ToList()).Should().Be("1 download failed in Films: Film 1.");
            DiscordText.ImportTroubleLine(ImportTrouble.ManualImport, null, titles.Take(1).ToList()).Should().Be("1 download is stuck and needs a manual import: Film 1.");
        }

        [Test]
        public void a_title_is_linked_only_where_a_browser_can_open_the_web_app()
        {
            var availability = new AvailabilitySettingsStore();
            var config = new Mock<IConfigService>();
            var links = new PortalLinks(config.Object, availability);

            links.Request(5).Should().BeNull("no address is known");
            availability.Set("http://requestarr:9797", null);
            links.Request(5).Should().BeNull("a container name opens nowhere");
            availability.Set("http://192.168.1.5:9797/requestarr", null);
            links.Request(5).Should().Be("http://192.168.1.5:9797/requestarr/requests/5");
            config.SetupGet(c => c.ApplicationUrl).Returns("https://requests.example/");
            links.Request(5).Should().Be("https://requests.example/requests/5");
        }
    }
}
