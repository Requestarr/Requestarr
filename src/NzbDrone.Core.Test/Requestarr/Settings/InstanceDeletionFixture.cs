using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.Settings;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Settings
{
    // Deleting a Sonarr keeps its history: its row stays as deleted, its requests still name it, those still waiting
    // need a new home, another instance can take over, and Requestarr's Connect entry is removed when it can be.
    [TestFixture]
    public class InstanceDeletionFixture : CoreTest<InstanceDeletion>
    {
        private static readonly Actor Admin = Actor.Web("owner");

        private BackendInstance _main;
        private BackendInstance _anime;
        private Mock<IBackendApi> _api;
        private List<Request> _requests;

        [SetUp]
        public void SetUp()
        {
            _main = new BackendInstance { Id = 1, Name = "Sonarr", Implementation = BackendImplementations.Sonarr, Enable = true, Settings = new BackendSettings { Url = "http://sonarr:8989", IsDefault = true } };
            _anime = new BackendInstance { Id = 2, Name = "Sonarr Anime", Implementation = BackendImplementations.Sonarr, Enable = true, Settings = new BackendSettings { Url = "http://anime:8989" } };
            _requests = new List<Request>
            {
                new() { Id = 10, BackendId = 1, Status = RequestStatus.Pending, Title = "Waiting" },
                new() { Id = 11, BackendId = 1, Status = RequestStatus.Approved, Title = "Downloading" },
                new() { Id = 12, BackendId = 1, Status = RequestStatus.Available, Title = "Done" }
            };

            var repository = Mocker.GetMock<IBackendRepository>();
            repository.Setup(r => r.Find(1)).Returns(_main);
            repository.Setup(r => r.Find(2)).Returns(_anime);
            repository.Setup(r => r.All()).Returns(() => new[] { _main, _anime }.Where(i => !i.IsDeleted));
            repository.Setup(r => r.Known()).Returns(() => new[] { _main, _anime, new BackendInstance { Id = 9, Name = "Sonarr Anime (deleted)" } });

            _api = new Mock<IBackendApi>();
            var backends = Mocker.GetMock<IBackendService>();
            backends.Setup(b => b.ApiFor(It.IsAny<BackendInstance>())).Returns(_api.Object);
            backends.Setup(b => b.Default(ContentType.Tv)).Returns(() => _main.IsDeleted ? _anime : _main);

            Mocker.GetMock<IRequestRepository>().Setup(r => r.WithStatus(It.IsAny<RequestStatus>())).Returns((RequestStatus status) => _requests.Where(r => r.Status == status).ToList());
        }

        [Test]
        public void the_confirm_knows_the_requests_waiting_and_who_can_take_over()
        {
            var preview = Subject.Preview(1);

            (preview.Name, preview.OpenRequests, preview.IsMain).Should().Be(("Sonarr", 2, true));
            preview.TakeOver.Select(t => t.Name).Should().Equal("Sonarr Anime");
        }

        [Test]
        public async Task deleting_keeps_the_history_flags_the_waiting_requests_and_hands_over_the_main_role()
        {
            var result = await Subject.DeleteAsync(1, 2, Admin);

            result.Should().Be(new InstanceDeleteResult(true, null, null, 2));
            (_main.IsDeleted, _main.Enable, _main.Config.IsDefault).Should().Be((true, false, false));
            _main.Name.Should().Be("Sonarr (deleted)", "its requests say where they went, and the name is free again");
            _anime.Config.IsDefault.Should().BeTrue("it takes over");

            _requests.Where(r => r.Id != 12).Should().OnlyContain(r => r.Status == RequestStatus.Failed && r.BackendId == 1 && r.DecisionReason == InstanceDeletion.NeedsNewHome("Sonarr"),
                "they keep where they went until Retry gives them a new home");
            _requests.Single(r => r.Id == 12).Should().Match<Request>(r => r.Status == RequestStatus.Available && r.BackendId == 1, "finished requests keep where they went");

            _api.Verify(a => a.RemoveWebhookAsync(_main.Config, It.IsAny<string>()), Times.Once());
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, InstanceDeletion.DeleteAction, "instance", "1", "Sonarr: deleted; Sonarr Anime takes over; 2 open requests need a new home"), Times.Once());
        }

        [Test]
        public async Task an_unreachable_instance_is_still_deleted_and_says_to_remove_the_connect_entry_by_hand()
        {
            _api.Setup(a => a.RemoveWebhookAsync(It.IsAny<BackendSettings>(), It.IsAny<string>())).ThrowsAsync(new BackendException("refused"));

            var result = await Subject.DeleteAsync(2, null, Admin);

            result.Deleted.Should().BeTrue();
            result.WebhookProblem.Should().Contain("remove \"Requestarr\" under Sonarr's Settings, then Connect, yourself");
            _anime.IsDeleted.Should().BeTrue();
            _anime.Name.Should().Be("Sonarr Anime (deleted 2)", "an earlier deleted instance has the first name");
            NzbDrone.Test.Common.ExceptionVerification.ExpectedWarns(1);
        }

        [Test]
        public async Task nothing_is_deleted_where_a_provisioning_file_lists_the_instances_or_for_a_stranger_taking_over()
        {
            (await Subject.DeleteAsync(1, 99, Admin)).Problem.Should().Contain("Choose another instance");

            Mocker.GetMock<IProvisioningService>().SetupGet(p => p.UsesFile).Returns(true);
            (await Subject.DeleteAsync(1, null, Admin)).Deleted.Should().BeFalse();

            _main.IsDeleted.Should().BeFalse();
        }
    }
}
