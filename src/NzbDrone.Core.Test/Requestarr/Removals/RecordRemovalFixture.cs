using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Admin;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Removals
{
    // A history request (Not in library, usually imported) is removed through the same vote as any title: its
    // requester is asked, everyone votes, Keep it keeps it, Force and Drop work. Passing drops the request from
    // Requestarr's lists and sends nothing to Sonarr or Radarr, whatever deleting files is set to.
    [TestFixture]
    public class RecordRemovalFixture : RemovalScenario
    {
        private static readonly Actor WebAdmin = Actor.Web("owner");

        private Request History(ulong owner = Alice) =>
            RequestStore.Insert(new Request
            {
                OwnerUserId = UserFor(owner).Id, ContentType = ContentType.Movie, Title = "Heat", Year = 1995, TmdbId = 949, Status = RequestStatus.NotInLibrary, Requested = Now.AddYears(-2)
            });

        private Task<ServiceResult> Start(Request request, bool force = false, IReadOnlyCollection<int> seasons = null) =>
            Removals.StartRecordByAdminAsync(WebAdmin, "owner (web app)", null, request.Id, seasons ?? new List<int>(), force);

        private void NothingSentToSonarrOrRadarr()
        {
            Radarr.Deletes.Should().BeEmpty();
            Sonarr.Deletes.Should().BeEmpty();
            Sonarr.SeasonDeletes.Should().BeEmpty();
        }

        [Test]
        public async Task a_passed_vote_drops_the_record_and_deletes_nothing_in_sonarr_or_radarr()
        {
            Mocker.Resolve<IConfigService>().RemovalDeleteFiles = true;
            var request = History();

            (await Start(request)).Message.Should().StartWith("Asked alice first");
            await Answer(Alice, yes: true);
            PromptOf(Bob).Content.Should().Contain("**Heat (1995)** may be removed from Requestarr's lists");

            await Tick(TimeSpan.FromDays(7));

            NothingSentToSonarrOrRadarr();
            Proposal().Status.Should().Be(RemovalStatus.Deleted);
            Proposal().RequestId.Should().BeNull("the request it was about is gone");
            RequestStore.Find(request.Id).Should().BeNull("it left Requestarr's lists");
            PromptOf(Bob).Content.Should().Contain("🗑️ **Removed** · **Heat (1995)** was removed from Requestarr's lists: nobody asked to keep it.");
            MessagesOf(Alice, "was removed from Requestarr's lists").Should().ContainSingle("the requester is told too");
            AuditTrail.Select(e => e.Action).Should().Contain("removal.vote-passed").And.Contain("removal.deleted").And.Contain(RequestService.DeleteRecordAction);
        }

        [Test]
        public async Task keep_it_keeps_the_record_and_drop_leaves_it()
        {
            var request = History();
            await Start(request);
            await Answer(Alice, yes: true);

            await Keep(Bob);
            await Tick(TimeSpan.FromDays(7));

            Proposal().Status.Should().Be(RemovalStatus.Flagged);
            RequestStore.Find(request.Id).Should().NotBeNull();

            await Removals.DropAsync(1, WebAdmin, "owner (web app)", null);

            Proposal().Status.Should().Be(RemovalStatus.Kept);
            RequestStore.Find(request.Id).Status.Should().Be(RequestStatus.NotInLibrary);
            NothingSentToSonarrOrRadarr();
        }

        [Test]
        public async Task with_deleting_files_off_force_still_drops_the_record_and_nothing_waits_to_be_deleted_by_hand()
        {
            Mocker.Resolve<IConfigService>().RemovalDeleteFiles = false;
            var request = History();

            var forced = await Start(request, force: true);

            forced.Message.Should().Be(DiscordText.RemovalRecordDropped(Proposal()));
            Proposal().Status.Should().Be(RemovalStatus.Forced, "not Delete it yourself: there are no files");
            RequestStore.Find(request.Id).Should().BeNull();
            Mocker.Resolve<IAdminQueue>().Items().Should().NotContain(i => i.Key == "x1");
            NothingSentToSonarrOrRadarr();
            DiscordText.RemovalConfirmForce(Proposal(), false).Should().Contain("nothing is deleted there").And.NotContain("delete it by hand");
        }

        [Test]
        public async Task a_record_is_removed_whole_and_one_removal_at_a_time()
        {
            var request = History();

            (await Start(request, seasons: new List<int> { 1 })).Message.Should().Be(DiscordText.RemovalRecordIsWhole);
            (await Start(request)).Success.Should().BeTrue();
            (await Start(request)).Message.Should().Be(DiscordText.RemovalAlreadyOpen(Proposal()));
            Proposals.All().Should().ContainSingle();
        }
    }
}
