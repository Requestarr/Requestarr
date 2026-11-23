using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using Requestarr.Core.Backends;
using Requestarr.Core.Metadata;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Framework;
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
                .ReturnsAsync(new MediaLookupResult { Overview = "A man returns.", PosterUrl = "https://image/crow.jpg", InCinemas = new DateTime(1994, 5, 11, 0, 0, 0, DateTimeKind.Utc), TitleSlug = "the-crow-9495" });
            Mocker.GetMock<ISonarrApi>().Setup(s => s.LookupTvdbAsync(It.IsAny<BackendSettings>(), 81189))
                .ReturnsAsync(new MediaLookupResult { Overview = "A teacher turns.", Network = "AMC", FirstAired = new DateTime(2008, 1, 20, 0, 0, 0, DateTimeKind.Utc), TitleSlug = "breaking-bad" });

            (await Subject.FillAsync()).Should().Be(3);

            var stored = Mocker.Resolve<IRequestRepository>().All().ToDictionary(r => r.Id);
            stored[movie.Id].Media.Should().BeEquivalentTo(new { Overview = "A man returns.", TitleSlug = "the-crow-9495" }, o => o.ExcludingMissingMembers());

            // The same moment, however the stored date comes back (UTC or this machine's time).
            stored[movie.Id].Media.InCinemas.Value.ToUniversalTime().Should().Be(new DateTime(1994, 5, 11, 0, 0, 0, DateTimeKind.Utc));
            stored[movie.Id].PosterUrl.Should().Be("https://image/crow.jpg");
            stored[show.Id].Media.Network.Should().Be("AMC");
            stored[missing.Id].Media.Overview.Should().BeNull("nothing found: looked up, never asked again");
            stored[known.Id].Media.Overview.Should().Be("Kept");

            (await Subject.FillAsync()).Should().Be(0);
            Mocker.GetMock<IRadarrApi>().Verify(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), It.IsAny<int>()), Times.Exactly(2));
        }

        [Test]
        public async Task a_request_imported_without_a_year_gets_the_year_its_title_first_came_out()
        {
            var smartHouse = Insert(ContentType.Movie, 35215, null, new RequestMedia
            {
                PhysicalRelease = new DateTime(2000, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                DigitalRelease = new DateTime(1999, 6, 25, 0, 0, 0, DateTimeKind.Utc),
                Checked = DateTime.UtcNow
            });
            var undated = Insert(ContentType.Movie, 1, null, new RequestMedia { Checked = DateTime.UtcNow });

            await Subject.FillAsync();

            var stored = Mocker.Resolve<IRequestRepository>().All().ToDictionary(r => r.Id);
            stored[smartHouse.Id].Year.Should().Be(1999);
            stored[undated.Id].Year.Should().BeNull("nothing says when it came out");
        }

        // Hollow Trees (#382 on dev): imported without a year, and Radarr had no date for it yet.
        [Test]
        public async Task a_title_with_no_release_date_in_radarr_gets_its_year_from_the_movie_database()
        {
            var hollowTrees = Insert(ContentType.Movie, 1650534, null, new RequestMedia { Checked = DateTime.UtcNow });
            Mocker.GetMock<ITmdbClient>().Setup(t => t.YearAsync(ContentType.Movie, 1650534, null, It.IsAny<CancellationToken>())).ReturnsAsync(2026);

            await Subject.FillAsync();

            Mocker.Resolve<IRequestRepository>().Get(hollowTrees.Id).Year.Should().Be(2026);
        }

        [Test]
        public async Task a_sonarr_or_radarr_that_cant_be_reached_is_asked_once_a_run_and_tried_again_next_time()
        {
            var movie = Insert(ContentType.Movie, 9495, null);
            Insert(ContentType.Movie, 603, null);
            Mocker.GetMock<IRadarrApi>().Setup(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), It.IsAny<int>())).ThrowsAsync(new BackendException("unreachable"));

            (await Subject.FillAsync()).Should().Be(0);

            Mocker.Resolve<IRequestRepository>().Get(movie.Id).Media.Should().BeNull();
            Mocker.GetMock<IRadarrApi>().Verify(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), It.IsAny<int>()), Times.Once(), "the rest of its requests wait for the next run");
        }

        [Test]
        public async Task a_title_radarr_answers_it_cant_find_is_marked_looked_up()
        {
            var movie = Insert(ContentType.Movie, 999999901, null);
            var request = new HttpRequest("http://radarr:7878/api/v3/movie/lookup/tmdb");
            var answer = new HttpResponse(request, new HttpHeader(), new CookieCollection(), "Not found", statusCode: HttpStatusCode.NotFound);
            Mocker.GetMock<IRadarrApi>().Setup(r => r.LookupTmdbAsync(It.IsAny<BackendSettings>(), It.IsAny<int>()))
                .ThrowsAsync(new BackendException("not found", new HttpException(answer)));

            (await Subject.FillAsync()).Should().Be(1);

            Mocker.Resolve<IRequestRepository>().Get(movie.Id).Media.Should().NotBeNull("Radarr answered: never asked again");
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
            TitleLinks.Open(SonarrInstance, new Request { ContentType = ContentType.Tv, TvdbId = 1, ExternalItemId = 22, Status = RequestStatus.Removed, Media = media }).Should().BeNull("removed from the library");

            var withBase = new BackendInstance { Implementation = BackendImplementations.Sonarr, Settings = new BackendSettings { Url = "http://sonarr:8989", BrowserUrl = "https://nas.example/sonarr/?x=1" } };
            TitleLinks.Open(withBase, show).Should().Be("https://nas.example/sonarr/series/breaking-bad", "the URL base is kept and any query left out");

            TitleLinks.Page(show).Should().Be("https://www.thetvdb.com/?tab=series&id=81189");
            TitleLinks.Page(movie).Should().Be("https://www.themoviedb.org/movie/9495");
        }
    }
}
