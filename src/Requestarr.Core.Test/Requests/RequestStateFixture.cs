using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // What a title's card says before anyone requests it: in the library, already requested (follow it instead), the
    // member's own request, or denied before.
    [TestFixture]
    public class RequestStateFixture : RequestScenario
    {
        private Task<InteractionRun> Detail(ulong user, int tmdbId) =>
            Harness.SendAsync(Search(user, "movie", $"tmdb:{tmdbId}"));

        [Test]
        public async Task a_title_in_the_library_brings_good_news_without_a_request_button()
        {
            var card = (await Detail(Alice, Arrival)).LastReply;

            card.Content.Should().Contain(DiscordText.StateInLibrary).And.Contain("Good news");
            card.Buttons.Should().BeEmpty();
        }

        [Test]
        public async Task duplicate_request_should_attach_subscriber_not_create_second_request()
        {
            await RequestMovie(Alice, Dune);

            var card = (await Detail(Bob, Dune)).LastReply;
            card.Content.Should().Contain(DiscordText.StateRequested("alice", RequestStatus.Pending, 1));
            card.Buttons.Single().Label.Should().Be(DiscordText.FollowLabel);

            var run = await RequestMovie(Bob, Dune);

            run.LastReply.Content.Should().Contain(DiscordText.Subscribed("Dune (2021)", "alice", 1));
            RequestStore.All().Should().ContainSingle();
            Mocker.Resolve<IRequestSubscriberRepository>().ForRequest(1).Single().UserId.Should().Be(UserFor(Bob).Id);
        }

        [Test]
        public async Task own_pending_request_shows_as_yours_with_a_cancel_button()
        {
            await RequestMovie(Alice, Dune);

            var card = (await Detail(Alice, Dune)).LastReply;

            card.Content.Should().Contain(DiscordText.StateYours(RequestStatus.Pending, 1));
            card.Buttons.Select(b => b.CustomId).Should().Equal("req-cancel:1");
        }

        [Test]
        public async Task owner_and_existing_subscriber_should_be_told_instead_of_duplicating()
        {
            await RequestMovie(Alice, Dune);
            await RequestMovie(Bob, Dune);

            (await RequestMovie(Alice, Dune)).LastReply.Content.Should().Contain(DiscordText.AlreadyRequestedByYou);
            (await RequestMovie(Bob, Dune)).LastReply.Content.Should().Contain(DiscordText.AlreadyFollowing);
            (await Detail(Bob, Dune)).LastReply.Content.Should().Contain(DiscordText.StateFollowing(1));
            Mocker.Resolve<IRequestSubscriberRepository>().All().Should().ContainSingle();
        }

        [Test]
        public async Task previously_denied_should_show_reason_and_allow_new_request()
        {
            await RequestMovie(Alice, Dune);
            await Harness.SendAsync(AdminSlash("deny").WithOption("id", 1L).WithOption("reason", "not in English"));

            var card = (await Detail(Bob, Dune)).LastReply;

            card.Content.Should().Contain(DiscordText.StatePreviouslyDenied("not in English"));
            card.Buttons.Single().Label.Should().Be(DiscordText.RequestLabel);

            (await RequestMovie(Bob, Dune)).LastReply.Content.Should().Contain(DiscordText.RequestPending("Dune (2021)", 2));
        }
    }
}
