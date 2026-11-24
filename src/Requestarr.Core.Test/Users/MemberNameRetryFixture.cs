using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Users
{
    // A channel rename Discord refuses is tried again later, on its own, so nothing waits for it; one that keeps failing
    // is logged and left for the next start. The names are stored whatever Discord says.
    [TestFixture]
    public class MemberNameRetryFixture : CoreTest<MemberNameService>
    {
        private const ulong Alice = 1001;
        private const ulong Channel = 2001;

        private static readonly TimeSpan RetriesWait = TimeSpan.FromSeconds(5);

        private readonly RequestarrUser _alice = new() { Id = 1, DisplayName = "alice", PrivateChannelId = Channel.ToString() };

        private GuildMember Member => new(Alice, "alice2", "alice", null, false, new List<ulong>());

        private Mock<IDiscordGuildGateway> Gateway => Mocker.GetMock<IDiscordGuildGateway>();

        [SetUp]
        public void SetUp()
        {
            Subject.RetryDelay = TimeSpan.Zero;
            Mocker.GetMock<IUserIdentityRepository>().Setup(r => r.Find(UserIdentity.DiscordProvider, Alice.ToString()))
                .Returns(new UserIdentity { UserId = 1, ExternalId = Alice.ToString(), Username = "alice" });
            Mocker.GetMock<IRequestarrUserRepository>().Setup(r => r.Find(1)).Returns(_alice);
            Mocker.GetMock<IAccessRequestRepository>().Setup(r => r.ForMember(It.IsAny<string>())).Returns(new List<AccessRequest>());
            Gateway.Setup(g => g.GetTextChannelsAsync()).ReturnsAsync(new List<GuildTextChannel> { new(Channel, "requests-alice", null) });
            Gateway.Setup(g => g.GetMemberAsync(Alice)).ReturnsAsync(Member);
        }

        // The retries run on their own: wait until the channel was renamed this many times, or give up.
        private async Task RenamedTimes(int times)
        {
            var clock = Stopwatch.StartNew();

            while (Gateway.Invocations.Count(i => i.Method.Name == nameof(IDiscordGuildGateway.RenameChannelAsync)) < times && clock.Elapsed < RetriesWait)
            {
                await Task.Delay(20);
            }

            await Task.Delay(100);
            Gateway.Verify(g => g.RenameChannelAsync(Channel, "requests-alice2"), Times.Exactly(times));
        }

        [Test]
        public async Task a_refused_rename_is_tried_again_without_holding_anything_up()
        {
            Gateway.SetupSequence(g => g.RenameChannelAsync(Channel, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Discord refused"))
                .Returns(Task.CompletedTask);

            (await Subject.RefreshAsync(Member)).Should().BeTrue();

            await RenamedTimes(2);
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task a_rename_that_keeps_failing_stops_after_the_last_attempt()
        {
            Gateway.Setup(g => g.RenameChannelAsync(Channel, It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Discord refused"));

            (await Subject.RefreshAsync(Member)).Should().BeTrue("the names are stored whatever Discord says");

            await RenamedTimes(MemberNameService.RenameAttempts);
            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(MemberNameService.RenameAttempts - 1);
        }
    }
}
