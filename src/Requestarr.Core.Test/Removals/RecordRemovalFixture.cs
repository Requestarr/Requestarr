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
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
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
            DiscordText.RemovalConfirmForce(Proposal(), false).Should().Contain("nothing is deleted").And.NotContain("delete it by hand");
        }

        [TestCase(RemovalStatus.Voting)]
        [TestCase(RemovalStatus.Declined)]
        [TestCase(RemovalStatus.Flagged)]
        public async Task force_on_an_open_vote_drops_the_record_even_with_deleting_files_off(RemovalStatus open)
        {
            Mocker.Resolve<IConfigService>().RemovalDeleteFiles = false;
            var request = History();
            await Start(request);
            await Answer(Alice, yes: open != RemovalStatus.Declined);

            if (open == RemovalStatus.Flagged)
            {
                await Keep(Bob);
                await Tick(TimeSpan.FromDays(7));
            }

            Proposal().Status.Should().Be(open);

            var forced = await Removals.ForceAsync(1, WebAdmin, "owner (web app)", null);

            forced.Message.Should().Be(DiscordText.RemovalRecordDropped(Proposal()));
            Proposal().Status.Should().Be(RemovalStatus.Forced, "not Delete it yourself: there are no files");
            RequestStore.Find(request.Id).Should().BeNull();
            NothingSentToSonarrOrRadarr();
        }

        [Test]
        public async Task dropping_the_record_unlinks_its_earlier_removals_too()
        {
            var request = History();
            await Start(request);
            await Answer(Alice, yes: false);
            await Removals.DropAsync(1, WebAdmin, "owner (web app)", null);

            await Start(request, force: true);

            RequestStore.Find(request.Id).Should().BeNull();
            Proposals.All().Select(p => (p.Status, p.RequestId)).Should().Equal(
                (RemovalStatus.Cancelled, (int?)null),
                (RemovalStatus.Forced, (int?)null));
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

        // Discord's admin panel: Remove a title finds the history request too, and its card starts the same removal.
        private async Task<InteractionReply> HistoryCardFor(string term)
        {
            var results = (await Harness.SendAsync(AsAdmin(FakeInteraction.ModalSubmit(RemovalRoutes.SearchForm).WithField(RemovalRoutes.SearchField, term)))).LastReply;
            var option = results.SelectMenu.Options.Single(o => o.Label == "Heat (1995) · Not in library");

            return (await Harness.SendAsync(AsAdmin(FakeInteraction.SelectMenu(RemovalRoutes.Pick, option.Value)))).LastReply;
        }

        [Test]
        public async Task remove_a_title_in_discord_finds_a_history_request_and_starts_its_removal()
        {
            var request = History();

            var card = await HistoryCardFor("hea");
            card.Content.Should().Contain("**Remove Heat (1995)** · Not in library").And.Contain("nothing is deleted");
            var started = (await Press(card, DiscordText.StartRemovalLabel)).LastReply;

            started.Content.Should().StartWith("Asked alice first");
            Proposal().RecordOnly.Should().BeTrue();
            Proposal().RequestId.Should().Be(request.Id);
            Proposal().InitiatedByAdmin.Should().BeTrue();
            NothingSentToSonarrOrRadarr();
        }

        [Test]
        public async Task force_on_the_discord_card_asks_first_then_drops_the_record()
        {
            var request = History();

            var confirm = (await Press(await HistoryCardFor("heat"), DiscordText.ForceRemovalLabel)).LastReply;
            confirm.Content.Should().Contain("Remove Heat (1995) from Requestarr's lists **now**?");
            var forced = (await Press(confirm, DiscordText.ForceRemovalLabel)).LastReply;

            forced.Content.Should().Be(DiscordText.RemovalRecordDropped(Proposal()));
            Proposal().Status.Should().Be(RemovalStatus.Forced);
            RequestStore.Find(request.Id).Should().BeNull("it left Requestarr's lists");
            NothingSentToSonarrOrRadarr();
        }

        [Test]
        public async Task the_feed_cards_delete_on_a_history_request_opens_its_history_card()
        {
            var request = History();
            var delete = RequestCards.FeedCard(request, null, "alice", null, new List<string>()).Buttons.Single(b => b.Label == DiscordText.DeleteLabel);

            var card = (await FeedClick(request.Id, delete.CustomId)).LastReply;

            card.Content.Should().Contain("**Remove Heat (1995)** · Not in library");
            card.Buttons.Select(b => b.CustomId).Should().Equal($"{RemovalRoutes.HistoryGo}:{request.Id}", $"{RemovalRoutes.HistoryForce}:{request.Id}");
        }
    }
}
