using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Imports;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Imports
{
    // /history and /admin history: a member's own imported requests in their own channel, and an admin's for one member or
    // the whole server.
    [TestFixture]
    public class HistoryCommandFixture : RequestScenario
    {
        [SetUp]
        public void AnswerAsPosted()
        {
            Mocker.GetMock<IHistoryPosting>().Setup(p => p.PostMember(It.IsAny<int>(), It.IsAny<Actor>())).Returns(ServiceResult.Ok("member posted"));
            Mocker.GetMock<IHistoryPosting>().Setup(p => p.PostServer(It.IsAny<Actor>())).Returns(ServiceResult.Ok("server posted"));
        }

        private RequestarrUser UserOf(ulong member) => Users.FindByDiscordId(member);

        [Test]
        public async Task a_member_gets_their_own_in_their_own_channel_privately()
        {
            var run = await Harness.SendAsync(UserCommand("history", Alice));

            run.Responder.Responses.Single().Ephemeral.Should().BeTrue();
            run.LastReply.Content.Should().Be("member posted");
            Mocker.GetMock<IHistoryPosting>().Verify(p => p.PostMember(UserOf(Alice).Id, It.IsAny<Actor>()), Times.Once());
        }

        [Test]
        public async Task elsewhere_or_as_someone_not_onboarded_it_posts_nothing()
        {
            var elsewhere = await Harness.SendAsync(FakeInteraction.SlashCommand("history").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(1));
            var stranger = await Harness.SendAsync(FakeInteraction.SlashCommand("history").By(Moderator).InGuild(FakeDiscordGuild.Guild).InChannel(1));

            elsewhere.LastReply.Content.Should().Be(DiscordText.OwnChannelOnly);
            stranger.LastReply.Content.Should().Be(DiscordText.NotAUser);
            Mocker.GetMock<IHistoryPosting>().Verify(p => p.PostMember(It.IsAny<int>(), It.IsAny<Actor>()), Times.Never());
        }

        [Test]
        public async Task an_admin_posts_one_members()
        {
            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin history").WithOption("member", Alice)));

            run.LastReply.Content.Should().Be("member posted");
            Mocker.GetMock<IHistoryPosting>().Verify(p => p.PostMember(UserOf(Alice).Id, It.IsAny<Actor>()), Times.Once());
        }

        [Test]
        public async Task an_admin_posts_the_whole_servers()
        {
            var run = await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin history")));

            run.LastReply.Content.Should().Be("server posted");
            run.Responder.Responses.Single().Ephemeral.Should().BeTrue();
        }

        [Test]
        public async Task someone_requestarr_doesnt_know_has_no_channel_and_a_member_cant_do_it_for_anyone()
        {
            var unknown = await Harness.SendAsync(AsAdmin(FakeInteraction.SlashCommand("admin history").WithOption("member", Moderator)));
            var member = await Harness.SendAsync(FakeInteraction.SlashCommand("admin history").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));

            unknown.LastReply.Content.Should().Be(DiscordText.HistoryNoChannel);
            member.LastReply.Content.Should().Be(DiscordText.AdminOnly);
            Mocker.GetMock<IHistoryPosting>().Verify(p => p.PostServer(It.IsAny<Actor>()), Times.Never());
        }
    }
}
