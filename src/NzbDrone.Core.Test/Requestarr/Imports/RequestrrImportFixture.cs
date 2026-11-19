using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Imports;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.AnimeDub;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Imports
{
    // Importing past requests from Requestrr's channels, from recorded channels shaped like Discord's API messages
    // (Recorded/, every case in the real channel's screenshots): who asked for what, matched to Sonarr and Radarr by ID.
    [TestFixture]
    public class RequestrrImportFixture : DbTest
    {
        private const ulong ChannelA = 900000000000100001;
        private const ulong ChannelB = 900000000000100002;
        private const ulong Riley = 900000000000200001;
        private const ulong Sam = 900000000000200002;
        private const int Radarr = 1;
        private const int Sonarr = 2;

        private static readonly LibraryTitle Crow = new(11, "The Crow", 1994, 9495, null, true, null, null, null);
        private static readonly LibraryTitle Severance = new(21, "Severance", 2022, null, 371980, false, new[] { 1, 2 }, Array.Empty<int>(), null);
        private static readonly LibraryTitle BreakingBad = new(22, "Breaking Bad", 2008, null, 81189, false, new[] { 1, 2, 3, 4, 5 }, new[] { 2 }, null);
        private static readonly LibraryTitle Nobody = new(12, "A Film Nobody Has", 2025, 999999901, null, false, null, null, null);

        private RecordedChannelHistory _recorded;

        private ImportService Subject => Mocker.Resolve<ImportService>();

        [SetUp]
        public void Setup()
        {
            _recorded = new RecordedChannelHistory(Item4To6Fixture.RepoFile("src", "NzbDrone.Core.Test", "Requestarr", "Imports", "Recorded"));
            Mocker.GetMock<IChannelHistory>().Setup(h => h.DescribeAsync(It.IsAny<ulong>())).ReturnsAsync((ulong id) => _recorded.Describe(id));
            Mocker.GetMock<IChannelHistory>().Setup(h => h.ReadAsync(It.IsAny<ulong>(), It.IsAny<ulong?>())).ReturnsAsync((ulong id, ulong? before) => _recorded.Read(id, before));

            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            Mocker.SetConstant<IUserIdentityRepository>(Mocker.Resolve<UserIdentityRepository>());
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IImportedMessageRepository>(Mocker.Resolve<ImportedMessageRepository>());
            Mocker.SetConstant<IUserManagementService>(Mocker.Resolve<UserManagementService>());

            GivenLibrary(Radarr, Crow);
            GivenLibrary(Sonarr, Severance, BreakingBad);
            Mocker.GetMock<IBackendService>().Setup(b => b.All()).Returns(new List<BackendInstance>
            {
                new() { Id = Radarr, Name = "Radarr (test)", Implementation = BackendImplementations.Radarr, Enable = true, Settings = new BackendSettings() },
                new() { Id = Sonarr, Name = "Sonarr (test)", Implementation = BackendImplementations.Sonarr, Enable = true, Settings = new BackendSettings() }
            });
        }

        private void GivenLibrary(int id, params LibraryTitle[] titles)
        {
            var api = new Mock<IBackendApi>();
            api.Setup(a => a.GetLibraryAsync(It.IsAny<BackendSettings>())).ReturnsAsync(titles.ToList());
            Mocker.GetMock<IBackendService>().Setup(b => b.ApiFor(It.Is<BackendInstance>(i => i.Id == id))).Returns(api.Object);
        }

        private async Task<ImportJobView> Preview(params ulong[] channels)
        {
            var job = await Subject.StartPreviewAsync(channels);

            for (var i = 0; i < 100 && Subject.Job(job.Id).State == ImportJobState.Reading; i++)
            {
                await Task.Delay(50);
            }

            return Subject.Job(job.Id);
        }

        private Task<ImportJobView> PreviewBoth() => Preview(ChannelA, ChannelB);

        // A Requestrr reply in channel A, a minute apart by ID, asked by who (null: no command member recorded).
        private static HistoryMessage Sent(ulong id, ulong? who, string seasons)
        {
            var at = new DateTime(2026, 11, 1, 9, 0, 0, DateTimeKind.Utc).AddMinutes(id % 1000);
            var embed = new HistoryEmbed("Breaking Bad (2008)", "https://www.thetvdb.com/?tab=series&id=81189");

            return new HistoryMessage(id, ChannelA, at, 900000000000300001, true, $"Your request for **{seasons}** of **Breaking Bad** was sent successfully!", new[] { embed }, who, who == Riley ? "Riley" : "Sam", Array.Empty<ulong>(), 3);
        }

        private void GivenChannelA(params HistoryMessage[] messages)
        {
            var newestFirst = messages.OrderByDescending(m => m.Id).ToList();
            Mocker.GetMock<IChannelHistory>().Setup(h => h.ReadAsync(ChannelA, null)).ReturnsAsync(newestFirst);
            Mocker.GetMock<IChannelHistory>().Setup(h => h.ReadAsync(ChannelA, newestFirst.Last().Id)).ReturnsAsync(Array.Empty<HistoryMessage>());
        }

        [Test]
        public void every_requestrr_reply_in_the_channels_is_read_for_what_it_says()
        {
            var source = new RequestrrSource();
            var read = _recorded.Read(ChannelA, null).Concat(_recorded.Read(ChannelB, null)).OrderBy(m => m.Id).Select(source.Read).ToList();

            read.Select(m => m.Kind).Should().Equal(
                SourceMessageKind.NotFound,
                SourceMessageKind.Abandoned,
                SourceMessageKind.AlreadyAvailable,
                SourceMessageKind.Sent,
                SourceMessageKind.Sent,
                SourceMessageKind.Sent,
                SourceMessageKind.Sent,
                SourceMessageKind.Other,
                SourceMessageKind.Sent,
                SourceMessageKind.Finished,
                SourceMessageKind.Sent,
                SourceMessageKind.AlreadyAvailable,
                SourceMessageKind.Sent);
            read[3].Should().BeEquivalentTo(new { Type = ContentType.Tv, Title = "Severance", Year = 2022, TvdbId = 371980, Seasons = Array.Empty<int>(), People = new[] { Riley } }, o => o.ExcludingMissingMembers());
            read[4].Seasons.Should().Equal(2);
            read[6].Should().BeEquivalentTo(new { Type = ContentType.Movie, Title = "The Crow", Year = 1994, TmdbId = 9495, People = new[] { Riley } }, o => o.ExcludingMissingMembers());
            read[9].People.Should().Equal(Riley, Sam);
            read[0].Message.InvokedByName.Should().Be("Sam", "who asked comes from the command's member, never the text");
        }

        [Test]
        public async Task the_preview_is_one_list_of_every_request_matched_or_not_and_writes_nothing()
        {
            var job = await PreviewBoth();
            var plan = job.Plan;

            job.State.Should().Be(ImportJobState.Ready);
            job.Channels.Select(c => (c.Read, c.Done)).Should().Equal((8, true), (5, true));
            plan.Requests.Select(r => (r.Title, r.Instance, r.Requester.Name, r.Outcome)).Should().Equal(
                ("Star Wars: The Last Jedi", null, "Sam", ImportOutcome.AlreadyAvailable),
                ("Severance", "Sonarr (test)", "Riley", ImportOutcome.Sent),
                ("Breaking Bad", "Sonarr (test)", "Riley", ImportOutcome.Sent),
                ("A Film Nobody Has", null, "Sam", ImportOutcome.Sent),
                ("The Crow", "Radarr (test)", "Riley", ImportOutcome.Sent),
                ("Another Lost Film", null, "Riley", ImportOutcome.Sent));
            plan.Requests.Select(r => r.InLibrary).Should().Equal(false, true, true, false, true, false);
            plan.Requests[0].Should().BeEquivalentTo(new { Type = ContentType.Movie, Year = 2017, TmdbId = 181808 }, o => o.ExcludingMissingMembers());
            plan.Requests[0].Followers.Select(f => f.Person.Name).Should().Equal(new[] { "Riley" }, "a second member's already available follows the first");
            plan.Requests[1].Seasons.Should().BeEmpty("all seasons");
            plan.Requests[2].Seasons.Should().Equal(2);
            plan.Requests[4].Followers.Select(f => f.Person.Name).Should().Equal(new[] { "Sam" }, "Riley asking again is the same request");
            plan.Requests[4].Available.Should().Be(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
            plan.NotRequests.Should().Be(2, "a search that found nothing and one left unpicked");
            plan.NoRequester.Should().Be(0);

            Mocker.Resolve<IRequestRepository>().All().Should().BeEmpty("nothing is written before the admin confirms");
            Mocker.Resolve<IRequestarrUserRepository>().All().Should().BeEmpty();
        }

        [Test]
        public async Task confirming_writes_every_request_with_its_old_date_and_a_second_run_adds_nothing()
        {
            var job = await PreviewBoth();
            var done = await Subject.ConfirmAsync(job.Id, Actor.Web("owner"));

            done.Result.Should().Be(new ImportResult(6, 3, 2, 2, 0));
            var requests = Mocker.Resolve<IRequestRepository>().All().OrderBy(r => r.Requested).ToList();
            requests.Select(r => (r.Title, r.Status, r.ExternalItemId, r.BackendId)).Should().Equal(
                ("Star Wars: The Last Jedi", RequestStatus.NotInLibrary, (int?)null, (int?)null),
                ("Severance", RequestStatus.Approved, 21, Sonarr),
                ("Breaking Bad", RequestStatus.Available, 22, Sonarr),
                ("A Film Nobody Has", RequestStatus.NotInLibrary, null, null),
                ("The Crow", RequestStatus.Available, 11, Radarr),
                ("Another Lost Film", RequestStatus.NotInLibrary, null, null));
            requests.Should().OnlyContain(r => r.Status == RequestStatus.NotInLibrary ? !r.IsActive : r.IsActive, "history never blocks a new request");
            requests[0].TmdbId.Should().Be(181808);
            requests[1].Requested.Should().Be(new DateTime(2026, 11, 7, 18, 15, 0, DateTimeKind.Utc));
            requests[1].Seasons.Should().Equal(1, 2);
            requests[1].MonitorFutureSeasons.Should().BeTrue();
            requests[4].Fulfilled.Should().Be(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));

            var people = Mocker.Resolve<IUserManagementService>();
            var riley = people.FindByDiscordId(Riley);
            var sam = people.FindByDiscordId(Sam);
            requests.Select(r => r.OwnerUserId).Should().Equal(sam.Id, riley.Id, riley.Id, sam.Id, riley.Id, riley.Id);
            new[] { riley, sam }.Should().OnlyContain(u => u.Status == UserStatus.Imported && u.PrivateChannelId == null && u.DiscordRoleId == null, "listed, never given a role or channel");
            Mocker.Resolve<IRequestSubscriberRepository>().ForRequest(requests[4].Id).Single().UserId.Should().Be(sam.Id);
            Mocker.Resolve<IRequestSubscriberRepository>().ForRequest(requests[0].Id).Single().UserId.Should().Be(riley.Id);

            var history = Mocker.Resolve<IAuditRepository>().ForTarget("request", requests[4].Id.ToString());
            history.Select(e => (e.Action, e.Timestamp)).Should().Equal(
                (ImportService.ImportedAction, new DateTime(2026, 11, 18, 7, 31, 0, DateTimeKind.Utc)),
                ("request.subscribe", new DateTime(2026, 11, 18, 10, 12, 0, DateTimeKind.Utc)),
                ("request.fulfilled", new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc)));
            Requestarr.Core.Portal.PortalText.Action(history[0].Action).Should().Be("Imported from Requestrr");
            Mocker.Resolve<IAuditRepository>().ForAction(ImportService.ImportAction).Single().Detail.Should().StartWith("6 requests (3 not in the library) and 2 followers from Requestrr");

            var again = await PreviewBoth();
            again.Plan.Requests.Should().BeEmpty();
            again.Plan.Follows.Should().BeEmpty();
            again.Plan.Links.Should().BeEmpty();
            again.Plan.NotRequests.Should().Be(0);
            again.Plan.AlreadyImported.Should().Be(12);

            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<InteractionReply>()), Times.Never(), "nothing is posted");
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.EditMessageAsync(It.IsAny<ulong>(), It.IsAny<ulong>(), It.IsAny<InteractionReply>()), Times.Never());
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.CreateRoleAsync(It.IsAny<string>()), Times.Never(), "nothing is created in Discord");
        }

        [Test]
        public async Task a_title_the_library_gains_later_is_linked_on_the_next_run()
        {
            await Subject.ConfirmAsync((await PreviewBoth()).Id, Actor.Web("owner"));
            GivenLibrary(Radarr, Crow, Nobody);

            var again = await PreviewBoth();
            again.Plan.Links.Should().ContainSingle().Which.Should().BeEquivalentTo(new { Title = "A Film Nobody Has (2025)", Instance = "Radarr (test)", ItemId = 12 }, o => o.ExcludingMissingMembers());
            var done = await Subject.ConfirmAsync(again.Id, Actor.Web("owner"));

            done.Result.Linked.Should().Be(1);
            var linked = Mocker.Resolve<IRequestRepository>().All().Single(r => r.Title == "A Film Nobody Has");
            (linked.Status, linked.BackendId, linked.ExternalItemId).Should().Be((RequestStatus.Approved, Radarr, 12));
            Mocker.Resolve<IAuditRepository>().ForTarget("request", linked.Id.ToString()).Last().Action.Should().Be(ImportService.LinkedAction);
        }

        [Test]
        public async Task people_are_listed_by_their_current_discord_name_and_whether_theyre_in_the_server()
        {
            var gateway = Mocker.GetMock<IDiscordGuildGateway>();
            gateway.SetupGet(g => g.IsConnected).Returns(true);
            gateway.Setup(g => g.GetMemberAsync(Riley)).ReturnsAsync(new GuildMember(Riley, "riley", "Riley Now", null, false, Array.Empty<ulong>()));
            gateway.Setup(g => g.GetMemberAsync(Sam)).ReturnsAsync((GuildMember)null);

            await Subject.ConfirmAsync((await PreviewBoth()).Id, Actor.Web("owner"));

            var people = Mocker.Resolve<IUserManagementService>();
            (people.FindByDiscordId(Riley).DisplayName, people.FindByDiscordId(Riley).Status).Should().Be(("Riley Now", UserStatus.Imported));
            people.FindByDiscordId(Sam).Status.Should().Be(UserStatus.ImportedLeft);
        }

        [Test]
        public async Task someone_imported_who_leaves_is_marked_gone_and_nothing_else_happens()
        {
            await Subject.ConfirmAsync((await PreviewBoth()).Id, Actor.Web("owner"));
            var people = Mocker.Resolve<IUserManagementService>();

            await people.HandleMemberLeftAsync(Riley);

            people.FindByDiscordId(Riley).Status.Should().Be(UserStatus.ImportedLeft);
            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<InteractionReply>()), Times.Never(), "no admin notice for someone who never joined Requestarr");
        }

        [Test]
        public async Task asking_for_a_title_requestarr_already_has_a_request_for_follows_it_instead()
        {
            // Waiting for approval (no Sonarr item yet), on another Sonarr: still the same title, by its TheTVDB ID.
            const int OtherSonarr = 3;
            var member = Mocker.Resolve<IUserManagementService>().EnsureRecord(900000000000200009, "jo", "Jo");
            var existing = Mocker.Resolve<IRequestRepository>().Insert(new Request
            {
                OwnerUserId = member.Id, ContentType = ContentType.Tv, Title = "Breaking Bad", TvdbId = 81189, BackendId = OtherSonarr, Status = RequestStatus.Pending, Requested = DateTime.UtcNow
            });

            var plan = (await PreviewBoth()).Plan;

            plan.Requests.Select(r => r.Title).Should().NotContain("Breaking Bad");
            plan.Follows.Single().Should().BeEquivalentTo(new { RequestId = existing.Id, Requester = "Jo", Followers = new[] { new { Person = new { Name = "Riley" } } } }, o => o.ExcludingMissingMembers());
        }

        [Test]
        public async Task seasons_asked_for_later_join_the_request_and_a_repeat_adds_nothing()
        {
            GivenChannelA(
                Sent(900000000001000101, Riley, "season 2"),
                Sent(900000000001000102, Riley, "season 3"),
                Sent(900000000001000103, Sam, "season 4"),
                Sent(900000000001000104, Riley, "season 2"),
                Sent(900000000001000105, null, "season 5"));

            var plan = (await Preview(ChannelA)).Plan;

            var request = plan.Requests.Single();
            request.Requester.Name.Should().Be("Riley");
            request.Seasons.Should().Equal(2, 3, 4);
            request.Followers.Select(f => f.Person.Name).Should().Equal("Sam");
            plan.NoRequester.Should().Be(1);
        }

        [Test]
        public async Task confirming_twice_at_once_writes_once()
        {
            var job = await PreviewBoth();

            var both = await Task.WhenAll(Subject.ConfirmAsync(job.Id, Actor.Web("owner")), Subject.ConfirmAsync(job.Id, Actor.Web("owner")));

            Mocker.Resolve<IRequestRepository>().All().Should().HaveCount(6);
            Mocker.Resolve<IAuditRepository>().All().Count(e => e.Action == ImportService.ImportAction).Should().Be(1);
            both.Select(v => v.Result.Requests).Should().Equal(6, 6);
        }

        [Test]
        public async Task a_channel_the_bot_cant_read_is_explained_and_nothing_is_read()
        {
            Mocker.GetMock<IChannelHistory>().Setup(h => h.DescribeAsync(ChannelA)).ReturnsAsync(new ImportChannel(ChannelA, "Old server", "media-requests", false, ImportText.NoMessageContent));

            var job = await Subject.StartPreviewAsync(new[] { ChannelA });

            job.State.Should().Be(ImportJobState.Failed);
            job.Problem.Should().Contain("Message Content Intent").And.Contain("Developer Portal");
            ImportText.CantSee("media-requests", "Old server").Should().Contain("View Channel").And.Contain("Read Message History");
            ImportText.NotFound("https://discord.com/invite").Should().Contain("isn't in that channel's server").And.Contain("https://discord.com/invite");
            Mocker.GetMock<IChannelHistory>().Verify(h => h.ReadAsync(It.IsAny<ulong>(), It.IsAny<ulong?>()), Times.Never());
        }

        [Test]
        public async Task replies_whose_text_discord_withholds_warn_that_message_content_is_off()
        {
            var withheld = new HistoryMessage(900000000001000099, ChannelA, DateTime.UtcNow, 900000000000300001, true, string.Empty, Array.Empty<HistoryEmbed>(), Riley, "Riley", Array.Empty<ulong>(), 0);
            Mocker.GetMock<IChannelHistory>().Setup(h => h.ReadAsync(ChannelA, null)).ReturnsAsync(new[] { withheld });
            Mocker.GetMock<IChannelHistory>().Setup(h => h.ReadAsync(ChannelA, withheld.Id)).ReturnsAsync(Array.Empty<HistoryMessage>());

            var job = await Preview(ChannelA);

            job.Warning.Should().Be(ImportText.NoMessageContent);
        }
    }
}
