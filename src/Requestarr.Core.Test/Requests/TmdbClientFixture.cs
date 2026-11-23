using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NLog;
using NLog.Config;
using NLog.Targets;
using NUnit.Framework;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Metadata;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Requests
{
    // The Movie Database client behind cast and years: how it authenticates, what it reads, where its key comes from,
    // and the health warning when the built-in key is refused. The key never reaches the log.
    [TestFixture]
    public class TmdbClientFixture : CoreTest
    {
        private const string Key = "v3-secret-key-123";

        [Test]
        public void a_read_access_token_goes_in_the_header_and_a_v3_key_in_the_query()
        {
            const string token = "eyJhbGciOiJIUzI1NiJ9.eyJhdWQiOiJ4In0.c2ln";

            using var bearer = TmdbClient.Build(ContentType.Tv, 196322, token);
            bearer.Headers.Authorization.Scheme.Should().Be("Bearer");
            bearer.RequestUri.ToString().Should().Be("https://api.themoviedb.org/3/tv/196322?append_to_response=aggregate_credits");

            using var v3 = TmdbClient.Build(ContentType.Movie, 438631, Key);
            v3.Headers.Authorization.Should().BeNull();
            v3.RequestUri.ToString().Should().Be($"https://api.themoviedb.org/3/movie/438631?append_to_response=credits&api_key={Key}");
        }

        [Test]
        public async Task the_client_reads_top_cast_and_episode_counts()
        {
            var client = Client(out _);
            client.Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"number_of_episodes\":9,\"aggregate_credits\":{\"cast\":[{\"name\":\"C\",\"order\":2},{\"name\":\"A\",\"order\":0},{\"name\":\"B\",\"order\":1},{\"name\":\"D\",\"order\":3}]}}")
            });

            var details = await client.GetAsync(ContentType.Tv, 196322, CancellationToken.None);

            details.Cast.Should().Equal("A", "B", "C");
            details.EpisodeCount.Should().Be(9);
        }

        // The year a title first came out: a film's release date, a show's first air date, and a show known only by its
        // TheTVDB ID found through The Movie Database's find.
        [Test]
        public async Task the_client_reads_the_year_a_title_first_came_out()
        {
            var client = Client(out _);
            var asked = new List<string>();
            client.Transport = (request, _) =>
            {
                asked.Add(request.RequestUri.AbsolutePath + request.RequestUri.Query.Split("api_key")[0]);
                var body = request.RequestUri.AbsolutePath.Contains("/find/")
                    ? "{\"tv_results\":[{\"first_air_date\":\"2017-09-25\"}]}"
                    : "{\"release_date\":\"2026-12-07\",\"first_air_date\":\"\"}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
            };

            (await client.YearAsync(ContentType.Movie, 1650534, null, CancellationToken.None)).Should().Be(2026);
            (await client.YearAsync(ContentType.Tv, null, 328724, CancellationToken.None)).Should().Be(2017);
            (await client.YearAsync(ContentType.Tv, 71446, null, CancellationToken.None)).Should().BeNull("no first air date");
            asked.Should().Equal("/3/movie/1650534?", "/3/find/328724?external_source=tvdb_id&", "/3/tv/71446?");
        }

        [Test]
        public async Task the_key_is_never_logged()
        {
            var client = Client(out var memory);

            client.Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized));
            (await client.GetAsync(ContentType.Movie, 1, CancellationToken.None)).Should().BeNull();

            client.Transport = (request, _) => throw new HttpRequestException($"failed {request.RequestUri}");
            (await client.GetAsync(ContentType.Movie, 2, CancellationToken.None)).Should().BeNull();

            memory.Logs.Should().HaveCount(2);
            memory.Logs.Should().OnlyContain(l => !l.Contains(Key));
            memory.Logs[0].Should().Contain("HTTP 401");
        }

        [Test]
        public void an_empty_key_disables_cast()
        {
            var store = new TmdbSettingsStore { Variable = _ => null, BuiltIn = () => null };

            store.Set("  ");
            store.Enabled.Should().BeFalse();
            store.Set($" {Key} ");
            store.ApiKey.Should().Be(Key);
        }

        [Test]
        public void the_key_is_the_provisioning_files_then_the_environments_then_the_built_in_one_then_none()
        {
            string variable = null;
            string builtIn = null;
            var store = new TmdbSettingsStore { Variable = _ => variable, BuiltIn = () => builtIn };

            (store.ApiKey, store.Source, store.Enabled).Should().Be((null, TmdbKeySource.None, false), "a fork's build, with nothing set");

            builtIn = " built-in \n";
            store.BuiltIn = () => builtIn;
            (store.ApiKey, store.Source).Should().Be(("built-in", TmdbKeySource.BuiltIn), "the image's own key, with no extra field");

            variable = " their-own ";
            (store.ApiKey, store.Source).Should().Be(("their-own", TmdbKeySource.Environment), "an admin's own key wins over the built-in one");

            store.Set("from-the-file");
            (store.ApiKey, store.Source).Should().Be(("from-the-file", TmdbKeySource.ProvisioningFile), "dev's and QA's provisioning file wins");
        }

        [Test]
        public async Task a_refused_built_in_key_raises_the_health_warning_until_it_works_again()
        {
            var store = new TmdbSettingsStore { Variable = _ => null, BuiltIn = () => "built-in" };
            var events = new Mock<IEventAggregator>();
            var client = new TmdbClient(store, events.Object, LogManager.CreateNullLogger());
            var check = new TmdbKeyCheck(store, Mock.Of<ILocalizationService>());
            var status = HttpStatusCode.Unauthorized;
            client.Transport = (_, _) => Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent("{}") });

            await client.GetAsync(ContentType.Movie, 603, CancellationToken.None);
            await client.GetAsync(ContentType.Movie, 603, CancellationToken.None);

            check.Check().Message.Should().Be(TmdbKeyCheck.Message("HTTP 401")).And.Contain(TmdbSettingsStore.KeyVariable);
            events.Verify(e => e.PublishEvent(It.IsAny<TmdbKeyChangedEvent>()), Times.Once(), "checked again only when it changes");

            status = HttpStatusCode.ServiceUnavailable;
            await client.GetAsync(ContentType.Movie, 603, CancellationToken.None);
            check.Check().Message.Should().Be(TmdbKeyCheck.Message("HTTP 401"), "The Movie Database being down says nothing about the key");

            status = HttpStatusCode.NotFound;
            await client.GetAsync(ContentType.Movie, 603, CancellationToken.None);

            check.Check().Type.Should().Be(HealthCheckResult.Ok, "a title it doesn't know says the key works");
            events.Verify(e => e.PublishEvent(It.IsAny<TmdbKeyChangedEvent>()), Times.Exactly(2));
        }

        [Test]
        public async Task an_admins_own_key_failing_raises_no_warning_about_the_built_in_one()
        {
            var store = new TmdbSettingsStore { Variable = _ => "their-own", BuiltIn = () => "built-in" };
            var client = new TmdbClient(store, Mock.Of<IEventAggregator>(), LogManager.CreateNullLogger());
            client.Transport = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.TooManyRequests));

            await client.GetAsync(ContentType.Movie, 603, CancellationToken.None);

            store.BuiltInProblem.Should().BeNull();
        }

        private static TmdbClient Client(out MemoryTarget memory)
        {
            memory = new MemoryTarget { Layout = "${message} ${exception:format=tostring}" };
            var config = new LoggingConfiguration();
            config.AddRuleForAllLevels(memory);
            var factory = new LogFactory { Configuration = config };

            var store = new TmdbSettingsStore();
            store.Set(Key);

            return new TmdbClient(store, Mock.Of<IEventAggregator>(), factory.GetLogger("tmdb-test"));
        }
    }
}
