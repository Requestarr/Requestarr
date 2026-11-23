using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;

namespace Requestarr.Core.Test.Requests
{
    // Live: which Sonarr takes a request, decided by what is asked for. Two connections, Everything and Anime, both on
    // the dev stack's TEST Sonarr; real lookups say whether a show is anime. The shows are removed from the test Sonarr
    // afterwards, pass or fail.
    //   set -a; . docker/secrets.env; set +a
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveRoutingFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveRoutingFixture : DiscordScenario
    {
        private const int OnePiece = 81797;
        private const int Severance = 371980;
        private const string EverythingName = "Sonarr (test)";
        private const string AnimeName = "Sonarr Anime (test)";

        private static string SonarrUrl => LiveDev.SonarrUrl;
        private static string SonarrKey => LiveDev.SonarrKey;

        protected override void UseBackends()
        {
            if (string.IsNullOrWhiteSpace(SonarrKey))
            {
                Assert.Ignore("SONARR_TEST_API_KEY not set");
            }

            UseRealHttp();
            Mocker.SetConstant<ISonarrApi>(Mocker.Resolve<SonarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Sonarr = new List<BackendInstanceProvisioning>
                {
                    Connection(EverythingName, "everything"),
                    Connection(AnimeName, "anime")
                }
            });
        }

        private static BackendInstanceProvisioning Connection(string name, string purpose) =>
            new() { Name = name, Url = SonarrUrl, ApiKey = SonarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/tv", SeriesType = "standard", Purpose = purpose };

        [Test]
        public async Task anime_goes_to_the_anime_sonarr_the_rest_to_everything_and_to_everything_with_anime_off()
        {
            var http = Mocker.Resolve<IHttpClient>();

            try
            {
                await RemoveFromSonarr(http, OnePiece);
                await RemoveFromSonarr(http, Severance);
                await AddUser(Alice);
                await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "tv").WithOption("level", "auto"));

                (await RequestedOn(OnePiece)).Should().Be(AnimeName, "One Piece is anime");
                (await RequestedOn(Severance)).Should().Be(EverythingName, "Severance isn't anime");

                await RemoveFromSonarr(http, OnePiece);
                var requests = Mocker.Resolve<IRequestRepository>();
                requests.DeleteMany(requests.All().Where(r => r.TvdbId == OnePiece).ToList());
                var backends = Mocker.Resolve<IBackendRepository>();
                var anime = backends.All().Single(b => b.Name == AnimeName);
                anime.Enable = false;
                backends.Update(anime);

                (await RequestedOn(OnePiece)).Should().Be(EverythingName, "with the Anime Sonarr turned off, anime goes to Everything");
            }
            finally
            {
                await RemoveFromSonarr(http, OnePiece);
                await RemoveFromSonarr(http, Severance);
            }
        }

        // Requests season 1 of a show as Alice, then names the instance the request went to, as stored.
        private async Task<string> RequestedOn(int tvdbId)
        {
            var run = await Harness.SendAsync(FakeInteraction.SelectMenu($"{RequestRoutes.Seasons}:{tvdbId}", "s1").By(Alice).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(Alice)));
            Log($"[route] {tvdbId}: {run.LastReply.Content.Replace("\n", " | ")}");

            var request = Mocker.Resolve<IRequestRepository>().All().Single(r => r.TvdbId == tvdbId);
            var instance = Mocker.Resolve<IBackendRepository>().Find(request.BackendId ?? 0)?.Name;
            Log($"[route] {tvdbId}: request #{request.Id} {request.Label} status={request.Status} went to {instance}");

            return instance;
        }

        private static async Task RemoveFromSonarr(IHttpClient http, int tvdbId)
        {
            var list = new HttpRequestBuilder($"{SonarrUrl}/api/v3/").Resource("series").SetHeader("X-Api-Key", SonarrKey).Build();

            foreach (var series in JArray.Parse((await http.GetAsync(list)).Content).Children<JObject>().Where(s => (int)s["tvdbId"] == tvdbId))
            {
                var request = new HttpRequestBuilder($"{SonarrUrl}/api/v3/").Resource($"series/{(int)series["id"]}?deleteFiles=true").SetHeader("X-Api-Key", SonarrKey).Build();
                request.Method = System.Net.Http.HttpMethod.Delete;
                await http.ExecuteAsync(request);
            }
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
