using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Access;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Users
{
    // A channel rename Discord refuses is tried again; one that keeps failing is logged and left for the next start,
    // without stopping the names from being stored.
    [TestFixture]
    public class MemberNameRetryFixture : CoreTest<MemberNameService>
    {
        private const ulong Alice = 1001;
        private const ulong Channel = 2001;

        private readonly RequestarrUser _alice = new() { Id = 1, DisplayName = "alice", PrivateChannelId = Channel.ToString() };

        private GuildMember Member => new(Alice, "alice2", "alice", null, false, new List<ulong>());

        [SetUp]
        public void SetUp()
        {
            Subject.RetryDelay = TimeSpan.Zero;
            Mocker.GetMock<IUserIdentityRepository>().Setup(r => r.Find(UserIdentity.DiscordProvider, Alice.ToString()))
                .Returns(new UserIdentity { UserId = 1, ExternalId = Alice.ToString(), Username = "alice" });
            Mocker.GetMock<IRequestarrUserRepository>().Setup(r => r.Find(1)).Returns(_alice);
            Mocker.GetMock<IAccessRequestRepository>().Setup(r => r.ForMember(It.IsAny<string>())).Returns(new List<AccessRequest>());
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.ChannelExistsAsync(Channel)).ReturnsAsync(true);
        }

        [Test]
        public async Task a_refused_rename_is_tried_again()
        {
            Mocker.GetMock<IDiscordGuildGateway>().SetupSequence(g => g.RenameChannelAsync(Channel, It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("Discord refused"))
                .Returns(Task.CompletedTask);

            (await Subject.RefreshAsync(Member)).Should().BeTrue();

            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.RenameChannelAsync(Channel, "requests-alice2"), Times.Exactly(2));
            ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task a_rename_that_keeps_failing_stops_after_the_last_attempt()
        {
            Mocker.GetMock<IDiscordGuildGateway>().Setup(g => g.RenameChannelAsync(Channel, It.IsAny<string>())).ThrowsAsync(new InvalidOperationException("Discord refused"));

            (await Subject.RefreshAsync(Member)).Should().BeTrue("the names are stored whatever Discord says");

            Mocker.GetMock<IDiscordGuildGateway>().Verify(g => g.RenameChannelAsync(Channel, It.IsAny<string>()), Times.Exactly(MemberNameService.RenameAttempts));
            ExceptionVerification.ExpectedErrors(1);
            ExceptionVerification.ExpectedWarns(MemberNameService.RenameAttempts - 1);
        }
    }
}
