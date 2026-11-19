using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // What each request's title is, looked up once and kept; and the links to its page in Sonarr or Radarr and on The
    // Movie Database or TheTVDB.
    [TestFixture]
    public class RequestMediaFixture : DbTest<RequestMediaService, Request>
    {
        private const int Radarr = 1;
        private const int Sonarr = 2;

        private static readonly BackendInstance RadarrInstance = new() { Id = Radarr, Name = "Radarr", Implementation = BackendImplementations.Radarr, Enable = true, Settings = new BackendSettings { Url = "http://radarr:7878" } };
        private static readonly BackendInstance SonarrInstance = new() { Id = Sonarr, Name = "Sonarr", Implementation = BackendImplementations.Sonarr, Enable = true, Settings = new BackendSettings { Url = "http://sonarr:8989", BrowserUrl = "http://nas.local:8989/" } };

        [SetUp]
        public void Setup()
        {
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.GetMock<IBackendService>().Setup(b => b.All()).Returns(new List<BackendInstance> { RadarrInstance, SonarrInstance });
            Subject.Delay = TimeSpan.Zero;
        }

        private Request Insert(ContentType type, int? tmdb, int? tvdb, RequestMedia media = null) =>
            Mocker.Resolve<IRequestRepository>().Insert(new Request
            {
                OwnerUserId = 1, ContentType = type, Title = "A title", TmdbId = tmdb, TvdbId = tvdb, Status = RequestStatus.NotInLibrary, Requested = DateTime.UtcNow, Media = media
            });

        [Test]
        public async Task requests_without_media_are_looked_up_once_and_kept()
        {
            var movie = Insert(ContentType.Movie, 9495, null);
            var show = Insert(ContentType.Tv, null, 81189);
            var missing = Insert(ContentType.Movie, 999999901, null);
            var known = Insert(ContentType.Movie, 603, null, new RequestMedia { Overview = "Kept", Checked = DateTime.UtcNow });
            Mocker.GetMock<IRadarrApi>().Setup(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), 9495))
                .ReturnsAsync(new MediaLookupResult { Overview = "A man returns.", PosterUrl = "https://image/crow.jpg", InCinemas = new DateTime(1994, 5, 11), TitleSlug = "the-crow-9495" });
            Mocker.GetMock<ISonarrApi>().Setup(s => s.LookupTvdbAsync(It.IsAny<BackendSettings>(), 81189))
                .ReturnsAsync(new MediaLookupResult { Overview = "A teacher turns.", Network = "AMC", FirstAired = new DateTime(2008, 1, 20), TitleSlug = "breaking-bad" });

            (await Subject.FillAsync()).Should().Be(3);

            var stored = Mocker.Resolve<IRequestRepository>().All().ToDictionary(r => r.Id);
            stored[movie.Id].Media.Should().BeEquivalentTo(new { Overview = "A man returns.", InCinemas = new DateTime(1994, 5, 11), TitleSlug = "the-crow-9495" }, o => o.ExcludingMissingMembers());
            stored[movie.Id].PosterUrl.Should().Be("https://image/crow.jpg");
            stored[show.Id].Media.Network.Should().Be("AMC");
            stored[missing.Id].Media.Overview.Should().BeNull("nothing found: looked up, never asked again");
            stored[known.Id].Media.Overview.Should().Be("Kept");

            (await Subject.FillAsync()).Should().Be(0);
            Mocker.GetMock<IRadarrApi>().Verify(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public async Task a_sonarr_or_radarr_that_cant_be_reached_is_tried_again_next_time()
        {
            var movie = Insert(ContentType.Movie, 9495, null);
            Mocker.GetMock<IRadarrApi>().Setup(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), It.IsAny<int>())).ThrowsAsync(new BackendException("unreachable"));

            (await Subject.FillAsync()).Should().Be(0);

            Mocker.Resolve<IRequestRepository>().Get(movie.Id).Media.Should().BeNull();
        }

        [Test]
        public void open_links_go_to_the_title_page_by_the_browser_address_and_never_to_a_container_name()
        {
            var media = new RequestMedia { TitleSlug = "breaking-bad", Checked = DateTime.UtcNow };
            var show = new Request { ContentType = ContentType.Tv, TvdbId = 81189, ExternalItemId = 22, Status = RequestStatus.Approved, Media = media };
            var movie = new Request { ContentType = ContentType.Movie, TmdbId = 9495, ExternalItemId = 11, Status = RequestStatus.Available };

            TitleLinks.Open(SonarrInstance, show).Should().Be("http://nas.local:8989/series/breaking-bad");
            TitleLinks.Open(RadarrInstance, movie).Should().BeNull("http://radarr:7878 only works from Requestarr");

            var reachable = new BackendInstance { Implementation = BackendImplementations.Radarr, Settings = new BackendSettings { Url = "http://192.168.1.5:7878" } };
            TitleLinks.Open(reachable, movie).Should().Be("http://192.168.1.5:7878/movie/9495", "an older request without a slug goes by its The Movie Database ID");
            TitleLinks.Open(SonarrInstance, new Request { ContentType = ContentType.Tv, TvdbId = 1, Status = RequestStatus.NotInLibrary, Media = media }).Should().BeNull("not in the library");

            TitleLinks.Page(show).Should().Be("https://www.thetvdb.com/?tab=series&id=81189");
            TitleLinks.Page(movie).Should().Be("https://www.themoviedb.org/movie/9495");
        }
    }
}
