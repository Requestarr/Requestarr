using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Lifecycle;
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
    // Imported history in Discord: each member's in their channel, a card for each request, when they're onboarded or
    // it's asked for, and every request in the admin feed oldest first between a header and a closing post. All silent,
    // never twice, paused and cancelled cleanly.
    [TestFixture]
    public class HistoryPostingFixture : DbTest<HistoryPosting, HistoryPost>
    {
        private const ulong Feed = 500;
        private const ulong AliceChannel = 600;
        private static readonly Actor Admin = Actor.Web("admin");
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

            Mocker.GetMock<IHistorySettings>().SetupGet(s => s.InFeed).Returns(true);
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

            Subject.PostServer(Admin).Message.Should().Be(DiscordText.HistoryQueued(3, false), "three requests: all in the feed, and Alice's two in her channel too (Bob isn't onboarded)");
            Subject.PostServer(Admin).Should().BeEquivalentTo(ServiceResult.Fail(DiscordText.HistoryAlreadyPosted), "nothing is ever queued twice");

            _sent.Should().OnlyContain(s => s.Message.Quiet);
            var channel = _sent.Where(s => s.Channel == AliceChannel).Select(s => s.Message).ToList();
            channel.Select(m => m.Content).Should().Equal(new[] { DiscordText.HistoryChannelHeader, null }, "the first card says what they are");
            channel.Select(m => m.Embeds.Single().Title).Should().Equal("Older", "Newer");
            channel[0].Embeds.Single().Description.Should().StartWith("Imported").And.Contain(DiscordText.FinalNotInLibrary);
            channel.Should().OnlyContain(m => m.Buttons.Count == 0);

            _sent.Where(s => s.Channel == Feed).Select(s => s.Message.Content).Should().Equal(
                DiscordText.HistoryFeedHeader(3, 2), "card Older", "card Bob's", "card Newer", DiscordText.HistoryFeedDone(3));
            Subject.Status().Should().Be(new HistoryPostingStatus(HistoryPostingState.Idle, 5, 5));
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<PostImportedHistoryCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Once());
        }

        [Test]
        public void a_resume_shows_the_run_under_system_tasks_again_even_while_the_worker_winds_down()
        {
            Imported(_alice, "Heat", 3);
            Subject.Background = _ => Task.CompletedTask;
            Subject.PostServer(Admin);
            Subject.Pause();

            Subject.Resume();

            Subject.Status().State.Should().Be(HistoryPostingState.Posting);
            Mocker.GetMock<IManageCommandQueue>().Verify(q => q.Push(It.IsAny<PostImportedHistoryCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()), Times.Exactly(2));
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
        public void the_feed_switched_off_is_left_out()
        {
            Mocker.GetMock<IHistorySettings>().SetupGet(s => s.InFeed).Returns(false);
            Imported(_alice, "Heat", 3);

            Subject.PostServer(Admin).Success.Should().BeTrue();

            _sent.Should().ContainSingle().Which.Channel.Should().Be(AliceChannel);
        }

        [Test]
        public void an_import_posts_in_the_feed_and_in_nobodys_channel()
        {
            Imported(_alice, "Heat", 3);

            Subject.QueueImport(0);

            _sent.Select(s => s.Channel).Should().OnlyContain(c => c == Feed);
        }

        [Test]
        public void a_members_history_is_posted_in_their_channel_once()
        {
            Imported(_alice, "Heat", 3);
            Imported(_bob, "Bob's", 10);

            Subject.PostMember(_alice.Id, Admin).Message.Should().Be(DiscordText.HistoryQueued(1, false));
            Subject.PostMember(_alice.Id, Admin).Should().BeEquivalentTo(ServiceResult.Fail(DiscordText.HistoryAlreadyPosted));

            _sent.Should().ContainSingle().Which.Channel.Should().Be(AliceChannel);
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, HistoryPosting.PostedAction, "user", _alice.Id.ToString(), DiscordText.HistoryAudit(1)), Times.Once());
        }

        [Test]
        public void someone_with_no_channel_or_no_imported_requests_is_told_so()
        {
            Imported(_bob, "Bob's", 10);

            Subject.PostMember(_bob.Id, Admin).Should().BeEquivalentTo(ServiceResult.Fail(DiscordText.HistoryNoChannel));
            var suspended = Mocker.Resolve<IRequestarrUserRepository>().Insert(new RequestarrUser { DisplayName = "Dan", Status = UserStatus.Suspended, PrivateChannelId = "800", Added = DateTime.UtcNow });
            Subject.PostMember(suspended.Id, Admin).Should().BeEquivalentTo(ServiceResult.Fail(DiscordText.HistorySuspended));
            Subject.PostMember(_alice.Id, Admin).Should().BeEquivalentTo(ServiceResult.Fail(DiscordText.HistoryNone));
            _sent.Should().BeEmpty();
        }

        [TestCase(true, 1)]
        [TestCase(false, 0)]
        public void onboarding_from_discord_posts_it_as_the_servers_setting_says(bool whenOnboarding, int sent)
        {
            Mocker.GetMock<IHistorySettings>().SetupGet(s => s.WhenOnboarding).Returns(whenOnboarding);
            Imported(_alice, "Heat", 3);

            Subject.Handle(new MemberOnboardedEvent(_alice.Id, OnboardHistory.AsSet));

            _sent.Should().HaveCount(sent);
        }

        [Test]
        public void onboarding_that_leaves_the_history_out_posts_nothing_until_its_asked_for()
        {
            Imported(_alice, "Heat", 3);

            Subject.Handle(new MemberOnboardedEvent(_alice.Id, OnboardHistory.Leave));
            _sent.Should().BeEmpty();

            Subject.PostMember(_alice.Id, Admin).Success.Should().BeTrue();
            _sent.Should().ContainSingle();
        }

        [Test]
        public void a_member_onboarded_later_gets_their_history_in_their_new_channel()
        {
            Mocker.GetMock<IHistorySettings>().SetupGet(s => s.InFeed).Returns(false);
            Imported(_bob, "Bob's", 10);
            Subject.PostServer(Admin).Should().BeEquivalentTo(ServiceResult.Fail(DiscordText.HistoryNowhere), "Bob has no channel yet, and the feed is off");

            _bob.Status = UserStatus.Active;
            _bob.PrivateChannelId = "700";
            Mocker.Resolve<IRequestarrUserRepository>().Update(_bob);
            Subject.Handle(new MemberOnboardedEvent(_bob.Id, OnboardHistory.Post));

            _sent.Should().ContainSingle().Which.Channel.Should().Be(700UL);
        }

        [Test]
        public void a_request_queued_while_posting_goes_out_in_the_same_run()
        {
            Imported(_bob, "First", 3);
            var queuedLate = false;
            Subject.Wait = _ =>
            {
                if (!queuedLate && _sent.Count == 1)
                {
                    queuedLate = true;
                    Imported(_bob, "Late", 1);
                    Subject.PostServer(Admin);
                }

                return Task.CompletedTask;
            };

            Subject.PostServer(Admin);

            _sent.Select(s => s.Message.Content).Should().Contain("card Late").And.EndWith(DiscordText.HistoryFeedDone(2));
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().BeEmpty();
            Subject.Status().State.Should().Be(HistoryPostingState.Idle);
        }

        [Test]
        public void a_failed_send_leaves_the_rest_waiting_and_posting_can_start_again()
        {
            Imported(_bob, "One", 3);
            Mocker.GetMock<IRequestFeed>().Setup(f => f.PostImportedAsync(It.IsAny<Request>())).ThrowsAsync(new System.InvalidOperationException("Discord is down"));

            Subject.PostServer(Admin);

            Subject.Status().State.Should().Be(HistoryPostingState.Idle);
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().ContainSingle();

            Subject.PostServer(Admin).Should().BeEquivalentTo(ServiceResult.Ok(DiscordText.HistoryUnderWay), "asking again carries on with what waited");
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void one_members_broken_channel_holds_nobody_else_up()
        {
            var carol = Mocker.Resolve<IRequestarrUserRepository>().Insert(new RequestarrUser { DisplayName = "Carol", Status = UserStatus.Active, PrivateChannelId = "700", Added = DateTime.UtcNow });
            Imported(_alice, "Heat", 3);
            Imported(carol, "Ronin", 2);
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.SendMessageAsync(AliceChannel, It.IsAny<InteractionReply>())).ThrowsAsync(new InvalidOperationException("Missing access"));

            Subject.PostServer(Admin);

            _sent.Select(s => s.Channel).Should().Contain(new[] { 700UL, Feed });
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().ContainSingle().Which.ChannelId.Should().Be(AliceChannel.ToString());
            Subject.Status().State.Should().Be(HistoryPostingState.Idle);

            // A later run's feed has its own header and count, though Alice's still waits.
            Imported(carol, "Collateral", 1);
            Subject.PostServer(Admin);

            _sent.Where(s => s.Channel == Feed).Select(s => s.Message.Content).Should().EndWith(DiscordText.HistoryFeedDone(1));
            Subject.Status().Should().Be(new HistoryPostingStatus(HistoryPostingState.Idle, 3, 2), "Alice's one is still part of it");
            ExceptionVerification.ExpectedWarns(2);
        }

        [Test]
        public void a_paused_posting_says_so_to_whoever_asks()
        {
            Imported(_alice, "Heat", 3);
            Subject.Background = _ => Task.CompletedTask;
            Subject.PostMember(_alice.Id, Admin);
            Subject.Pause();

            Subject.PostMember(_alice.Id, Admin).Should().BeEquivalentTo(ServiceResult.Ok(DiscordText.HistoryPaused));
        }

        [Test]
        public void a_restart_part_way_carries_on_without_a_second_feed_header()
        {
            Imported(_bob, "One", 3);
            Imported(_bob, "Two", 2);
            var isDown = true;
            Mocker.GetMock<IRequestFeed>().Setup(f => f.PostImportedAsync(It.Is<Request>(r => r.Title == "Two"))).ReturnsAsync((Request r) =>
            {
                if (isDown)
                {
                    throw new InvalidOperationException("Requestarr stopped");
                }

                _sent.Add((Feed, new InteractionReply { Content = $"card {r.Title}", Quiet = true }));
                return (ulong)_sent.Count;
            });
            Subject.PostServer(Admin);
            _sent.Should().HaveCount(2, "the header and one card went out before it stopped");
            ExceptionVerification.ExpectedWarns(1);

            isDown = false;
            Subject.Handle(new ApplicationStartedEvent());

            _sent.Select(s => s.Message.Content).Should().Equal(DiscordText.HistoryFeedHeader(2, 1), "card One", "card Two", DiscordText.HistoryFeedDone(2));
        }

        [Test]
        public void a_members_history_paused_part_way_carries_on_without_saying_again_what_it_is()
        {
            Imported(_alice, "One", 3);
            Imported(_alice, "Two", 2);
            Subject.Wait = _ =>
            {
                if (_sent.Count == 1)
                {
                    Subject.Pause();
                }

                return Task.CompletedTask;
            };
            Subject.PostMember(_alice.Id, Admin);
            _sent.Should().ContainSingle();

            Subject.Wait = _ => Task.CompletedTask;
            Subject.Resume();

            _sent.Select(s => s.Message.Content).Should().Equal(DiscordText.HistoryChannelHeader, null);
        }

        [Test]
        public void a_shows_card_says_its_seasons_and_shows_its_poster_and_nothing_only_the_admins_see()
        {
            Mocker.Resolve<IRequestRepository>().Insert(new Request { OwnerUserId = _alice.Id, Title = "Landman", PosterUrl = "https://example.com/p.jpg", ContentType = ContentType.Tv, Status = RequestStatus.NotInLibrary, Imported = true, MonitorFutureSeasons = true, BackendId = 7, Requested = DateTime.UtcNow });

            Subject.PostMember(_alice.Id, Admin);

            var card = _sent.Single().Message.Embeds.Single();
            card.ThumbnailUrl.Should().Be("https://example.com/p.jpg");
            card.Description.Split('\n').Should().HaveCount(4).And.EndWith(DiscordText.SeasonsLine("all seasons"), "every season was asked for, and no season is named");
            card.Description.Should().NotContain("Alice");
        }

        [Test]
        public void a_request_changed_or_deleted_while_posting_goes_out_as_it_is_by_then_or_not_at_all()
        {
            Imported(_alice, "One", 3);
            var two = Imported(_alice, "Two", 2);
            var three = Imported(_alice, "Three", 1, RequestStatus.NotInLibrary);
            Subject.Wait = _ =>
            {
                if (_sent.Count == 1)
                {
                    Mocker.Resolve<IRequestRepository>().Delete(two.Id);
                    three.Status = RequestStatus.Available;
                    Mocker.Resolve<IRequestRepository>().Update(three);
                }

                return Task.CompletedTask;
            };

            Subject.PostMember(_alice.Id, Admin);

            _sent.Select(s => s.Message.Embeds.Single().Title).Should().Equal("One", "Three");
            _sent[1].Message.Embeds.Single().Description.Should().Contain(DiscordText.FinalAvailable);
            Subject.Status().Should().Be(new HistoryPostingStatus(HistoryPostingState.Idle, 2, 2));
        }

        [Test]
        public void a_request_deleted_while_posting_is_paused_holds_nothing_up()
        {
            var gone = Imported(_alice, "Gone", 3);
            Imported(_alice, "Here", 2);
            Subject.Wait = _ =>
            {
                Subject.Pause();
                return Task.CompletedTask;
            };
            Subject.PostMember(_alice.Id, Admin);
            Mocker.Resolve<IRequestRepository>().Delete(gone.Id);

            Subject.Wait = _ => Task.CompletedTask;
            Subject.Resume();

            _sent.Select(s => s.Message.Embeds.Single().Title).Should().Equal("Here");
        }

        [Test]
        public void a_pause_in_a_members_channel_starts_nothing_in_the_feed()
        {
            Imported(_alice, "One", 3);
            Subject.Wait = _ =>
            {
                Subject.Pause();
                return Task.CompletedTask;
            };

            Subject.PostServer(Admin);

            _sent.Should().BeEmpty();
        }

        [Test]
        public void cancel_stops_part_way_with_a_closing_post_and_drops_what_waited()
        {
            Imported(_bob, "One", 3);
            Imported(_bob, "Two", 2);
            Imported(_bob, "Three", 1);
            Subject.Wait = _ =>
            {
                if (_sent.Count == 2)
                {
                    Subject.Cancel(Admin);
                }

                return Task.CompletedTask;
            };

            Subject.PostServer(Admin);

            _sent.Select(s => s.Message.Content).Should().Equal(DiscordText.HistoryFeedHeader(3, 1), "card One", DiscordText.HistoryFeedStopped(1, 3));
            Mocker.Resolve<IHistoryPostRepository>().Waiting().Should().BeEmpty();
        }
    }
}
