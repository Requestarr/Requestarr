using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Requests
{
    // An approved request goes to the type's instance with that instance's quality profile, root folder and series type,
    // whoever asked, and the instance's item ID is recorded on the request. A failed add says so.
    [TestFixture]
    public class SendToMediaManagerFixture : RequestScenario
    {
        [SetUp]
        public async Task AutoApprove()
        {
            await SetAccess(Alice, "movie", "auto");
            await SetAccess(Alice, "tv", "auto");
        }

        [Test]
        public async Task movie_should_use_instance_profile_and_root_and_record_item_id()
        {
            await RequestMovie(Alice, Dune);

            var add = Radarr.Adds.Single();
            add.Url.Should().Be("http://radarr-test:7878");
            add.QualityProfileId.Should().Be(4);
            add.RootFolder.Should().Be("/data/media");

            LastRequest.ExternalItemId.Should().Be(101);
            LastRequest.QualityProfileId.Should().Be(4);
            LastRequest.RootFolderPath.Should().Be("/data/media");
            LastRequest.BackendId.Should().Be(Instance("Radarr").Id);
        }

        [Test]
        public async Task every_members_requests_go_to_the_types_everything_instance()
        {
            await SetAccess(Bob, "movie", "auto");

            await RequestMovie(Alice, Dune);
            await RequestMovie(Bob, DuneTwo);

            Radarr.Adds.Select(a => a.Url).Should().Equal(new[] { "http://radarr-test:7878", "http://radarr-test:7878" }, "Radarr is for Everything; Radarr 4K for nothing yet");
        }

        [Test]
        public async Task series_should_use_instance_series_type()
        {
            await RequestTv(Alice, Severance, "all");

            Sonarr.Adds.Single().SeriesType.Should().Be("anime");
            Sonarr.Adds.Single().QualityProfileId.Should().Be(4);
            LastRequest.ExternalItemId.Should().Be(101);
        }

        [Test]
        public async Task backend_failure_should_mark_request_failed_and_tell_user()
        {
            Radarr.FailAdds = true;

            var run = await RequestMovie(Alice, Dune);

            LastRequest.Status.Should().Be(RequestStatus.Failed);
            LastRequest.DecisionReason.Should().Contain("validation failed");
            run.LastReply.Content.Should().Contain("Request #1 for Dune (2021) couldn't be added");
            ExceptionVerification.ExpectedErrors(1);
        }
    }
}
