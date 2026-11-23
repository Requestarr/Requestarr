using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Admin;
using Requestarr.Core.Discord;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Test.Removals
{
    // "Delete files in media managers" off (the default, and beta's): a removal does everything but delete, then waits
    // in the admin queue for an admin to delete it by hand.
    [TestFixture]
    public class KeepFilesRemovalFixture : RemovalScenario
    {
        [SetUp]
        public void DeletingOff()
        {
            Mocker.Resolve<IConfigService>().RemovalDeleteFiles = false;
        }

        private bool Waiting(int proposal = 1) => Mocker.Resolve<IAdminQueue>().Items().Any(i => i.Key == $"x{proposal}");

        [Test]
        public async Task a_vote_that_passes_deletes_nothing_and_asks_an_admin_to_delete_it_by_hand()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);

            await Tick(TimeSpan.FromDays(7));

            Radarr.Deletes.Should().BeEmpty("deleting is off");
            Proposal().Status.Should().Be(RemovalStatus.DeleteByHand);
            Reload(request.Id).Status.Should().Be(RequestStatus.Available, "the request stays until it's really gone");
            PromptOf(Bob).Content.Should().Contain("🗑️ **Removal agreed** · **Dune (2021)** will be removed from the library. Deleting is turned off on this server, so an admin deletes it by hand.");
            Line(CardOf(request.Id), "🗑️ Remov").Should().Contain("deleting is turned off on this server");
            CardOf(request.Id).Buttons.Select(b => b.CustomId).Should().Equal("rm-by-hand:1", "rm-drop:1");
            Waiting().Should().BeTrue();

            var walk = (await Walk()).LastReply;
            walk.Content.Should().Contain("**Delete it yourself:**").And.Contain("Delete it from Radarr");
            walk.Buttons.Select(b => b.Label).Should().Contain(DiscordText.DeletedByHandLabel).And.Contain(DiscordText.DropRemovalLabel).And.NotContain(DiscordText.ForceRemovalLabel);

            var done = (await Press(walk, DiscordText.DeletedByHandLabel)).LastReply;

            done.Content.Should().Contain(DiscordText.RemovalDeletedByHand(Proposal()));
            Proposal().Status.Should().Be(RemovalStatus.Deleted, "it was the vote's");
            Reload(request.Id).Status.Should().Be(RequestStatus.Removed);
            Radarr.Deletes.Should().BeEmpty("the admin deleted it, not Requestarr");
            Waiting().Should().BeFalse();
            AuditTrail.Select(e => e.Action).Should().Contain("removal.delete-by-hand").And.Contain("removal.deleted-by-hand");
        }

        [Test]
        public async Task force_waits_for_the_admin_too_and_stays_the_admins()
        {
            var request = await DownloadedMovie();

            var forced = (await AdminStart(request, force: true)).LastReply;

            forced.Content.Should().Contain("Deleting is turned off on this server, so nothing was deleted");
            Radarr.Deletes.Should().BeEmpty();
            Proposal().Status.Should().Be(RemovalStatus.DeleteByHand);

            await Press((await Walk()).LastReply, DiscordText.DeletedByHandLabel);

            Proposal().Status.Should().Be(RemovalStatus.Forced);
        }

        [Test]
        public async Task force_on_one_already_waiting_is_refused_until_deleting_is_turned_on()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            await Tick(TimeSpan.FromDays(7));
            var told = AuditTrail.Count;

            (await Removals.ForceAsync(1, Requestarr.Core.Audit.Actor.System, "owner", null)).Message.Should().Be(DiscordText.RemovalAlreadyByHand(Proposal()));
            AuditTrail.Count.Should().Be(told, "nothing happens a second time");

            Mocker.Resolve<IConfigService>().RemovalDeleteFiles = true;
            await Removals.ForceAsync(1, Requestarr.Core.Audit.Actor.System, "owner", null);

            Radarr.Deletes.Should().Equal((Instance("Radarr").Config.Url, request.ExternalItemId.Value));
            Proposal().Status.Should().Be(RemovalStatus.Forced);
        }

        [Test]
        public void forcing_says_what_happens_with_deleting_off()
        {
            DiscordText.RemovalConfirmForce("**Dune (2021)**", Array.Empty<int>(), false).Should().Contain("Deleting is turned off on this server, so nothing is deleted").And.NotContain("this can't be undone");
            DiscordText.RemovalConfirmForce("**Dune (2021)**", Array.Empty<int>(), true).Should().Contain("Its files are deleted");
        }

        [Test]
        public async Task every_card_that_starts_a_removal_says_deleting_is_off()
        {
            var request = await DownloadedMovie();

            (await UserRemove(Alice, request.Id)).LastReply.Content.Should().Contain(DiscordText.DeletingOffNote);
            (await FeedClick(request.Id, $"{RemovalRoutes.Feed}:{request.Id}")).LastReply.Content.Should().Contain(DiscordText.DeletingOffNote);
        }

        [Test]
        public async Task dropping_one_waiting_to_be_deleted_keeps_it()
        {
            var request = await DownloadedMovie();
            await UserStartVote(Alice, request.Id);
            await Tick(TimeSpan.FromDays(7));

            await Press((await Walk()).LastReply, DiscordText.DropRemovalLabel);

            Proposal().Status.Should().Be(RemovalStatus.Cancelled);
            Reload(request.Id).Status.Should().Be(RequestStatus.Available);
            Waiting().Should().BeFalse();
        }
    }
}
