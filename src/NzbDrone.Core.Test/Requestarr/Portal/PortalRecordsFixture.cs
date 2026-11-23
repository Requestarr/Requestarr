using System;
using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Access;
using Requestarr.Core.Announcements;
using Requestarr.Core.Backends;
using Requestarr.Core.Portal;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Portal
{
    // People asking to join, outages and each version's changes, as the portal's pages show them.
    [TestFixture]
    public class PortalRecordsFixture : CoreTest<PortalRecords>
    {
        private static readonly DateTime Start = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

        [Test]
        public void a_paused_person_is_named_as_they_last_asked_to_join()
        {
            Mocker.GetMock<IAccessRequestRepository>().Setup(r => r.Pending()).Returns(new List<AccessRequest>
            {
                new() { Id = 4, DiscordUserId = "1", Username = "carol", DisplayName = "Carol", ContentTypes = new List<ContentType> { ContentType.Movie }, Requested = Start }
            });
            Mocker.GetMock<IWelcomeGuardRepository>().Setup(g => g.AllBlocked()).Returns(new List<WelcomeGuard> { new() { DiscordUserId = "2", Blocked = Start } });
            Mocker.GetMock<IAccessRequestRepository>().Setup(r => r.ForMember("2")).Returns(new List<AccessRequest> { new() { Username = "dave", DisplayName = "Dave" } });

            var requests = Subject.AccessRequests();

            requests.Waiting.Should().ContainSingle().Which.Name.Should().Be("Carol");
            requests.Paused.Should().ContainSingle().Which.Should().Be(new PortalBlocked("2", "Dave", Start));
        }

        [Test]
        public void outages_now_are_the_instances_alerted_as_down_and_history_is_every_outage()
        {
            Mocker.GetMock<IBackendService>().Setup(b => b.All()).Returns(new List<BackendInstance>
            {
                new() { Id = 1, Name = "Radarr", Enable = true, DownSince = Start, DownAlertMessageId = "8", DownAnnounceOffered = true, DownAnnounceMessageId = "9" },
                new() { Id = 2, Name = "Sonarr", Enable = true },
                new() { Id = 3, Name = "Lidarr", Enable = true, DownSince = Start }
            });
            Mocker.GetMock<IOutageRepository>().Setup(o => o.Ongoing(1)).Returns(new Outage { AnnouncedBy = "owner (web app)" });
            Mocker.GetMock<IOutageRepository>().Setup(o => o.History()).Returns(new List<Outage> { new() { InstanceName = "Radarr", Started = Start } });

            var outages = Subject.Outages();

            outages.Now.Should().ContainSingle().Which.Should().Be(new PortalOutageNow(1, "Radarr", Start, true, true, "owner (web app)"));
            outages.History.Should().ContainSingle().Which.Instance.Should().Be("Radarr");
        }

        [Test]
        public void instances_say_what_they_are_for_and_what_new_members_get_there()
        {
            var radarr = new BackendInstance { Id = 1, Name = "Radarr", Implementation = BackendImplementations.Radarr, Enable = true, Settings = new BackendSettings { DefaultAccess = AccessState.AutoApprove } };
            var radarr4K = new BackendInstance { Id = 2, Name = "Radarr 4K", Implementation = BackendImplementations.Radarr, Enable = true, Settings = new BackendSettings { Purpose = InstancePurpose.Nothing } };
            Mocker.GetMock<IBackendService>().Setup(b => b.All()).Returns(new List<BackendInstance> { radarr, radarr4K });
            Mocker.GetMock<IBackendService>().Setup(b => b.Default(ContentType.Movie)).Returns(radarr);

            Subject.Instances().Should().Equal(
                new PortalInstance(1, "Radarr", ContentType.Movie, true, InstancePurpose.Everything, AccessState.AutoApprove),
                new PortalInstance(2, "Radarr 4K", ContentType.Movie, true, InstancePurpose.Nothing, AccessState.NeedsApproval));
        }

        [Test]
        public void whats_new_has_each_version_as_its_card_labels_it()
        {
            var version = new ChangelogVersion(new Version(0, 7, 4), new List<string> { "**A:** b" }, new List<string>(), Start);
            Mocker.GetMock<IAnnouncementService>().Setup(a => a.VersionNotes()).Returns(new List<(ChangelogVersion, string)> { (version, "0.7.0004") });

            Subject.Changes().Should().ContainSingle().Which.Should().BeEquivalentTo(new { Version = "0.7.0004", Date = (DateTime?)Start, Users = new[] { "**A:** b" } });
        }
    }
}
