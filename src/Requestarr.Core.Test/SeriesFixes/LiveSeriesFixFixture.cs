using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Datastore.Migration;
using Requestarr.Core.Backends;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Requests;
using Requestarr.Core.SeriesFixes;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Test.Common.Categories;

namespace Requestarr.Core.Test.SeriesFixes
{
    // Live: the shipped Pokémon fix built in the dev stack's TEST Sonarr only when Pokémon is requested, reused for the
    // next request, and left out once turned off, read back from Sonarr. The show, the fix's tag and its profile are
    // removed afterwards, pass or fail; the owner's own tags and profiles are never touched.
    //   set -a; . docker/secrets.env; set +a
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveSeriesFixFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveSeriesFixFixture : DiscordScenario
    {
        private const int Pokemon = series_fixes.PokemonTvdbId;
        private const string TagName = "requestarr-fix-pokemon";
        private const string ProfileName = "Requestarr fix: Pokémon (1997)";

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
            Mocker.SetConstant<ISeriesFixRepository>(Mocker.Resolve<SeriesFixRepository>());
            Mocker.SetConstant<ISeriesFixService>(Mocker.Resolve<SeriesFixService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Sonarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Sonarr (test)", Url = SonarrUrl, ApiKey = SonarrKey, DefaultAccess = "approval", QualityProfile = "HD-1080p", RootFolder = "/data/tv", SeriesType = "standard", Purpose = "everything" }
                }
            });
        }

        [Test]
        public async Task the_fix_is_built_on_request_reused_after_and_left_out_when_off()
        {
            var http = Mocker.Resolve<IHttpClient>();

            try
            {
                await CleanUp(http);
                (await Names(http, "tag")).Should().NotContain(TagName, "nothing is made before anyone asks");
                await AddUser(Alice);
                await AddUser(Bob);
                await Harness.SendAsync(AdminCommand("user access", Alice).WithOption("type", "tv").WithOption("level", "auto"));
                await Harness.SendAsync(AdminCommand("user access", Bob).WithOption("type", "tv").WithOption("level", "auto"));

                var reply = await Request(Alice);
                var tagId = await TagId(http);
                (await SeriesTags(http)).Should().Contain(tagId);
                (await Names(http, "releaseprofile")).Count(n => n == ProfileName).Should().Be(1);
                Requests().Single().AppliedFix.Should().NotBeNull();
                reply.Should().Contain("Series fix:");
                Log($"[fix] built: tag {TagName}={tagId}, profile \"{ProfileName}\"; card: {reply.Replace("\n", " | ")}");

                await Request(Bob);
                (await Names(http, "tag")).Count(n => n == TagName).Should().Be(1);
                (await Names(http, "releaseprofile")).Count(n => n == ProfileName).Should().Be(1);
                Log("[fix] reused: one tag and one profile after a second request");

                var fixes = Mocker.Resolve<ISeriesFixRepository>();
                var fix = fixes.ForSeries(Pokemon);
                fix.Enabled = false;
                fixes.Update(fix);
                await RemoveSeries(http);
                Mocker.Resolve<IRequestRepository>().DeleteMany(Requests());

                await Request(Alice);
                (await SeriesTags(http)).Should().NotContain(tagId);
                Requests().Single().AppliedFix.Should().BeNull();
                Log("[fix] off: added again without the fix's tag");
            }
            finally
            {
                await CleanUp(http);
            }
        }

        private async Task<string> Request(ulong user)
        {
            var run = await Harness.SendAsync(FakeInteraction.SelectMenu($"{RequestRoutes.Seasons}:{Pokemon}", "s1").By(user).InGuild(FakeDiscordGuild.Guild).InChannel(ChannelOf(user)));
            return run.LastReply.Content;
        }

        private List<Request> Requests() => Mocker.Resolve<IRequestRepository>().All().Where(r => r.TvdbId == Pokemon).ToList();

        private static async Task<int> TagId(IHttpClient http) =>
            (int)(await Get(http, "tag")).Children<JObject>().Single(t => (string)t["label"] == TagName)["id"];

        private static async Task<List<int>> SeriesTags(IHttpClient http) =>
            (await Get(http, "series")).Children<JObject>().Single(s => (int)s["tvdbId"] == Pokemon)["tags"].Select(t => (int)t).ToList();

        private static async Task<List<string>> Names(IHttpClient http, string resource) =>
            (await Get(http, resource)).Children<JObject>().Select(i => (string)(i["label"] ?? i["name"])).ToList();

        // Only what this check made: the show, and the tag and profile named as the Pokémon fix.
        private static async Task CleanUp(IHttpClient http)
        {
            await RemoveSeries(http);

            foreach (var profile in (await Get(http, "releaseprofile")).Children<JObject>().Where(p => (string)p["name"] == ProfileName))
            {
                await Delete(http, $"releaseprofile/{(int)profile["id"]}");
            }

            foreach (var tag in (await Get(http, "tag")).Children<JObject>().Where(t => (string)t["label"] == TagName))
            {
                await Delete(http, $"tag/{(int)tag["id"]}");
            }
        }

        private static async Task RemoveSeries(IHttpClient http)
        {
            foreach (var series in (await Get(http, "series")).Children<JObject>().Where(s => (int)s["tvdbId"] == Pokemon))
            {
                await Delete(http, $"series/{(int)series["id"]}?deleteFiles=true");
            }
        }

        private static async Task<JArray> Get(IHttpClient http, string resource)
        {
            var request = new HttpRequestBuilder($"{SonarrUrl}/api/v3/").Resource(resource).SetHeader("X-Api-Key", SonarrKey).Build();
            return JArray.Parse((await http.GetAsync(request)).Content);
        }

        private static Task Delete(IHttpClient http, string resource)
        {
            var request = new HttpRequestBuilder($"{SonarrUrl}/api/v3/").Resource(resource).SetHeader("X-Api-Key", SonarrKey).Build();
            request.Method = System.Net.Http.HttpMethod.Delete;
            return http.ExecuteAsync(request);
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
