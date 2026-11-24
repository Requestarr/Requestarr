using System;
using System.Linq;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // A history request (Not in library, from an import) whose removal passed leaves Requestarr's records with its
    // follows, and the audit log keeps that it went. A live request is never deleted this way.
    [TestFixture]
    public class DeleteRecordFixture : DbTest<RequestService, Request>
    {
        private static readonly Actor Admin = Actor.Web("owner");

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
        }

        private Request Insert(RequestStatus status) =>
            Mocker.Resolve<IRequestRepository>().Insert(new Request
            {
                OwnerUserId = 1, ContentType = ContentType.Movie, Title = "Dune", Year = 2021, Status = status, Requested = DateTime.UtcNow
            });

        [Test]
        public void a_history_request_is_deleted_with_its_follows_and_audited()
        {
            var old = Insert(RequestStatus.NotInLibrary);
            var kept = Insert(RequestStatus.NotInLibrary);
            var follows = Mocker.Resolve<IRequestSubscriberRepository>();
            follows.Insert(new RequestSubscriber { RequestId = old.Id, UserId = 2, Added = DateTime.UtcNow });
            follows.Insert(new RequestSubscriber { RequestId = kept.Id, UserId = 2, Added = DateTime.UtcNow });

            Subject.DeleteRecord(old.Id, Admin);

            Mocker.Resolve<IRequestRepository>().All().Select(r => r.Id).Should().Equal(kept.Id);
            follows.All().Select(f => f.RequestId).Should().Equal(kept.Id);
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(Admin, RequestService.DeleteRecordAction, "request", old.Id.ToString(), "Dune (2021)"), Times.Once());
        }

        [Test]
        public void a_live_request_keeps_its_record()
        {
            var live = Insert(RequestStatus.Approved);

            Subject.DeleteRecord(live.Id, Admin);

            Mocker.Resolve<IRequestRepository>().All().Should().ContainSingle();
            Mocker.GetMock<IAuditService>().Verify(a => a.Record(It.IsAny<Actor>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()), Times.Never());
        }
    }
}
