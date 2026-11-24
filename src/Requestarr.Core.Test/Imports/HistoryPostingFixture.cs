using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Imports;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Imports
{
    // Imported history in Discord: each member's in their channel, a few titles to a message, and every request in the
    // admin feed oldest first between a header and a closing post; silent; never twice; paused and cancelled cleanly.
    [TestFixture]
    public class HistoryPostingFixture : DbTest<HistoryPosting, HistoryPost>
    {
        private const ulong Feed = 500;
        private const ulong AliceChannel = 600;
        private readonly List<(ulong Channel, InteractionReply Message)> _sent = new();
        private RequestarrUser _alice;
        private RequestarrUser _bob;

        [SetUp]
        public void SetUp()
        {
            _sent.Clear();
            Mocker.SetConstant<IHistoryPostRepository>(Mocker.Resolve<HistoryPostRepository>());
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            var users = Mocker.Resolve<IRequestarrUserRepository>();
            _alice = users.Insert(new RequestarrUser { DisplayName = "Alice", Status = UserStatus.Active, PrivateChannelId = AliceChannel.ToString(), Added = DateTime.UtcNow });
            _bob = users.Insert(new RequestarrUser { DisplayName = "Bob", Status = UserStatus.Imported, Added = DateTime.UtcNow });

            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(c => c.ImportPostFeed).Returns(true);
            config.SetupGet(c => c.ImportPostChannels).Returns(true);
            Mocker.GetMock<IDiscordGuildGateway>().SetupGet(g => g.GuildId).Returns(1UL);
            Mocker.GetMock<IDiscordGuildConfigRepository>().Setup(c => c.FindByGuild("1")).Returns(new DiscordGuildConfig { FeedChannelId = Feed.ToString() });
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.SendMessageAsync(It.IsAny<ulong>(), It.IsAny<InteractionReply>()))
                .ReturnsAsync((ulong channel, InteractionReply message) =>
                {
                    _sent.Add((channel, message));
                    return (ulong)_sent.Count;
                });
            Mocker.GetMock<IRequestFeed>().Setup(f => f.PostImportedAsync(It.IsAny<Request>())).ReturnsAsync((Request r) =>
            {
                _sent.Add((Feed, new InteractionReply { Content = $"card {r.Title}", Quiet = true }));
                return (ulong)_sent.Count;
            });

            // The worker runs at once instead of in the background, with no pause between posts.
            Subject.Background = work => work();
            Subject.Wait = _ => Task.CompletedTask;
        }

        private Request Imported(RequestarrUser owner, string title, int daysAgo, RequestStatus status = RequestStatus.Available) =>
            Mocker.Resolve<IRequestRepository>().Insert(new Request { OwnerUserId = owner.Id, Title = title, ContentType = ContentType.Movie, Status = status, Imported = true, Requested = DateTime.UtcNow.AddDays(-daysAgo) });

        [Test]
        public void members_channels_and_the_feed_get_every_imported_request_once_oldest_first_and_silent()
        {
            Imported(_alice, "Newer", 1);
            Imported(_alice, "Older", 30, RequestStatus.NotInLibrary);
            Imported(_bob, "Bob's", 10);
            Mocker.Resolve<IRequestRepository>().Insert(new Request { OwnerUserId = _alice.Id, Title = "Made here", Requested = DateTime.UtcNow });

            Subject.QueueEarlier().Should().Be(5, "three in the feed, and Alice's two in her channel (Bob isn't onboarded)");
            Subject.QueueEarlier().Should().Be(0, "nothing is ever queued twice");

            _sent.Should().OnlyContain(s => s.Message.Quiet);
            var channel = _sent.Where(s => s.Channel == AliceChannel).Select(s => s.Message.Content).Single();
            channel.Should().StartWith(DiscordText.HistoryChannelHeader);
            channel.IndexOf("Older", StringComparison.Ordinal).Should().BeLessThan(channel.IndexOf("Newer", StringComparison.Ordinal));
            channel.Should().Contain("not in library");

            _sent.Where(s => s.Channel == Feed).Select(s => s.Message.Content).Should().Equal(
                DiscordText.HistoryFeedHeader(3, 2), "card Older", "card Bob's", "card Newer", DiscordText.HistoryFeedDone(3));
            Subject.Status().Should().Be(new HistoryPostingStatus(HistoryPostingState.Idle, 5, 5));
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<PostImportedHistoryCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void system_tasks_shows_the_run_with_its_progress_until_it_finishes_or_is_paused()
        {
            var statuses = new Queue<HistoryPostingStatus>(new[]
            {
                new HistoryPostingStatus(HistoryPostingState.Posting, 467, 311),
                new HistoryPostingStatus(HistoryPostingState.Posting, 467, 312),
                new HistoryPostingStatus(HistoryPostingState.Paused, 467, 312)
            });
            var waits = 0;
            Mocker.GetMock<IHistoryPosting>().Setup(p => p.Status()).Returns(statuses.Dequeue);
            var progress = Mocker.Resolve<PostImportedHistoryProgress>();
            progress.Wait = _ =>
            {
                waits++;
                return Task.CompletedTask;
            };

            progress.Execute(new PostImportedHistoryCommand());

            (waits, statuses.Count).Should().Be((2, 0), "it follows the run while it posts, and stops once it's paused");
            PostImportedHistoryProgress.Progress(new HistoryPostingStatus(HistoryPostingState.Posting, 467, 312)).Should().Be("Imported history: 312 of 467 posted");
            PostImportedHistoryProgress.Progress(new HistoryPostingStatus(HistoryPostingState.Paused, 467, 312)).Should().EndWith("312 of 467 posted, paused");
        }

        [Test]
        public void a_switch_turned_off_leaves_that_destination_out()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportPostFeed).Returns(false);
            Imported(_alice, "Heat", 3);

            Subject.QueueEarlier().Should().Be(1);

            _sent.Should().ContainSingle().Which.Channel.Should().Be(AliceChannel);
        }

        [Test]
        public void a_member_onboarded_later_gets_their_history_in_their_new_channel()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportPostFeed).Returns(false);
            Imported(_bob, "Bob's", 10);
            Subject.QueueEarlier().Should().Be(0, "Bob has no channel yet");

            _bob.Status = UserStatus.Active;
            _bob.PrivateChannelId = "700";
            Mocker.Resolve<IRequestarrUserRepository>().Update(_bob);
            Subject.Handle(new MemberOnboardedEvent(_bob.Id));

            _sent.Should().ContainSingle().Which.Channel.Should().Be(700UL);
        }

        [Test]
        public void a_request_queued_while_posting_goes_out_in_the_same_run()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportPostChannels).Returns(false);
            Imported(_alice, "First", 3);
            var queuedLate = false;
            Subject.Wait = _ =>
            {
                if (!queuedLate && _sent.Count == 1)
                {
                    queuedLate = true;
                    Imported(_alice, "Late", 1);
                    Subject.QueueEarlier();
                }

                return Task.CompletedTask;
            };

            Subject.QueueEarlier();

            _sent.Select(s => s.Message.Content).Should().Contain("card Late").And.EndWith(DiscordText.HistoryFeedDone(2));
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().BeEmpty();
            Subject.Status().State.Should().Be(HistoryPostingState.Idle);
        }

        [Test]
        public void a_failed_send_leaves_the_rest_waiting_and_posting_can_start_again()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportPostChannels).Returns(false);
            Imported(_alice, "One", 3);
            Mocker.GetMock<IRequestFeed>().Setup(f => f.PostImportedAsync(It.IsAny<Request>())).ThrowsAsync(new System.InvalidOperationException("Discord is down"));

            Subject.QueueEarlier();

            Subject.Status().State.Should().Be(HistoryPostingState.Idle);
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().ContainSingle();
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public void cancel_stops_part_way_with_a_closing_post_and_drops_what_waited()
        {
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ImportPostChannels).Returns(false);
            Imported(_alice, "One", 3);
            Imported(_alice, "Two", 2);
            Imported(_alice, "Three", 1);
            Subject.Wait = _ =>
            {
                if (_sent.Count == 2)
                {
                    Subject.Cancel(Actor.Web("admin"));
                }

                return Task.CompletedTask;
            };

            Subject.QueueEarlier();

            _sent.Select(s => s.Message.Content).Should().Equal(DiscordText.HistoryFeedHeader(3, 1), "card One", DiscordText.HistoryFeedStopped(1, 3));
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().BeEmpty();
        }
    }
}
