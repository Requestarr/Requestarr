using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Announcements;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Qa;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Welcome;

namespace Requestarr.Core.Test.Qa
{
    // The QA checklist has a channel group of its own, Requestarr QA, admin-only like Requestarr Admin, on the QA server
    // only; dev, beta and production get none (dev's goes). An existing checklist channel is moved there with
    // its cards, on the next start.
    [TestFixture]
    public class QaGroupFixture : WelcomeScenario
    {
        private FakeDiscordGuild.FakeChannel Group(string name) => Discord.Channels.Values.SingleOrDefault(c => c.IsCategory && c.Name == name);

        [Test]
        public void production_and_beta_have_no_qa_group()
        {
            Group(ChannelLayout.QaCategoryName).Should().BeNull();
            Discord.ChannelNamed(QaText.ChecklistChannelName).Should().BeNull();
        }

        [Test]
        public async Task the_checklist_already_in_the_admin_group_moves_to_its_own_group_with_its_cards()
        {
            var admin = Group(ChannelLayout.AdminCategoryName);
            var checklist = await Discord.CreateTextChannelAsync(QaText.ChecklistChannelName, admin.Id, QaText.ChecklistChannelTopic, admin.Overwrites);
            var card = await Discord.SendMessageAsync(checklist, new InteractionReply { Cards = { new InteractionCard { Text = "## QA checklist" } } });
            var config = Config;
            config.QaChecklistChannelId = checklist.ToString();
            config.QaChecklistSummaryMessageId = card.ToString();
            Mocker.Resolve<IDiscordGuildConfigRepository>().Upsert(config);

            await StartOnQa();
            await StartOnQa();

            var qa = Group(ChannelLayout.QaCategoryName);
            var channel = Discord.Channels[checklist];
            channel.CategoryId.Should().Be(qa.Id, "moved, not recreated");
            channel.MessageIds.Should().Equal(card);
            Config.QaChecklistSummaryMessageId.Should().Be(card.ToString(), "its cards are kept");
            Config.QaCategoryId.Should().Be(qa.Id.ToString());
            qa.Overwrites.Should().BeEquivalentTo(admin.Overwrites, "admin-only, like Requestarr Admin");
            channel.Overwrites.Should().BeEquivalentTo(qa.Overwrites);
            Discord.Channels.Values.Count(c => c.IsCategory && c.Name == ChannelLayout.QaCategoryName).Should().Be(1);
            Discord.ChannelsIn(admin.Id).Select(c => c.Name).Should().BeEquivalentTo(ChannelLayout.AdminChannelName, ChannelLayout.FeedChannelName);
        }

        [Test]
        public async Task a_new_qa_server_gets_the_group_and_the_checklist_in_it()
        {
            await StartOnQa();

            Discord.ChannelNamed(QaText.ChecklistChannelName).CategoryId.Should().Be(Group(ChannelLayout.QaCategoryName).Id);
        }

        [Test]
        public async Task the_dev_server_removes_its_checklist_channel_and_group_and_a_live_check_can_make_one_for_itself()
        {
            await StartOn(VersionPostMode.NotYetStaged);
            var checklist = Discord.ChannelNamed(QaText.ChecklistChannelName).Id;
            await Discord.SendMessageAsync(checklist, new InteractionReply { Cards = { new InteractionCard { Text = "## QA checklist" } } });

            await StartOn(VersionPostMode.CurrentOnly);

            Discord.ChannelNamed(QaText.ChecklistChannelName).Should().BeNull("the checklist is the owner's QA work, on the QA server only");
            Group(ChannelLayout.QaCategoryName).Should().BeNull();
            new[] { Config.QaChecklistChannelId, Config.QaCategoryId, Config.QaChecklistSummaryMessageId }.Should().OnlyContain(id => id == null);

            await Mocker.Resolve<IGuildSetupService>().EnsureQaChecklistAsync();
            Discord.ChannelNamed(QaText.ChecklistChannelName).CategoryId.Should().Be(Group(ChannelLayout.QaCategoryName).Id);
            await Mocker.Resolve<IGuildSetupService>().RemoveQaChecklistAsync();
            new[] { Discord.ChannelNamed(QaText.ChecklistChannelName), Group(ChannelLayout.QaCategoryName) }.Should().OnlyContain(c => c == null);
        }

        private Task StartOnQa() => StartOn(VersionPostMode.NotYetStaged);

        private Task StartOn(VersionPostMode posts)
        {
            Mocker.GetMock<IReleaseEnvironment>().SetupGet(e => e.VersionPosts).Returns(posts);
            return Mocker.Resolve<IGuildSetupService>().EnsureAsync();
        }
    }
}
