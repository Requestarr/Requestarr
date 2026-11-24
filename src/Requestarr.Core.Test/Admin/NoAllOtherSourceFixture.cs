using System;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Admin;
using Requestarr.Core.Backends;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Admin
{
    // The admin queue's item for a type whose instance for all other requests is gone: it starts when an instance check
    // finds nobody has that purpose, keeps its time across a restart, and ends when an instance takes it.
    [TestFixture]
    public class NoAllOtherSourceFixture : CoreTest<NoAllOtherSource>
    {
        private static readonly DateTime Started = new(2026, 11, 24, 9, 0, 0, DateTimeKind.Utc);

        private readonly BackendInstance _anime = new() { Id = 2, Implementation = BackendImplementations.Sonarr };
        private BackendInstance _allOther;
        private string _stored;

        [SetUp]
        public void SetUp()
        {
            _allOther = null;
            _stored = string.Empty;
            var config = Mocker.GetMock<IConfigService>();
            config.SetupGet(c => c.TvAllOtherMissingSince).Returns(() => _stored);
            config.SetupSet(c => c.TvAllOtherMissingSince = It.IsAny<string>()).Callback<string>(value => _stored = value ?? string.Empty);
            config.SetupGet(c => c.MovieAllOtherMissingSince).Returns(string.Empty);
            Mocker.GetMock<IBackendService>().Setup(b => b.AllOther(ContentType.Tv)).Returns(() => _allOther);
            Mocker.GetMock<IBackendService>().Setup(b => b.Default(ContentType.Tv)).Returns(() => _allOther ?? _anime);
            Subject.Clock = () => Started;
        }

        [Test]
        public void it_waits_from_when_nobody_took_the_purpose_until_an_instance_does()
        {
            Subject.Handle(new BackendsCheckedEvent());

            var restarted = Mocker.Resolve<NoAllOtherSource>();
            restarted.Clock = () => Started.AddDays(1);
            restarted.Items().Should().Equal(new QueueItem(NoAllOtherSource.Key(ContentType.Tv), Started));

            _allOther = new BackendInstance { Id = 1, Implementation = BackendImplementations.Sonarr };
            Subject.Handle(new BackendsCheckedEvent());

            Subject.Items().Should().BeEmpty();
            _stored.Should().BeEmpty();
            Mocker.GetMock<IEventAggregator>().Verify(e => e.PublishEvent(It.IsAny<AdminQueueChangedEvent>()), Times.Exactly(2), "the admin panel's count follows");
        }
    }
}
