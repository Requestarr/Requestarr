using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Imports;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;
using Requestarr.Test.Common.Categories;
using ChannelPermissions = Requestarr.Core.Discord.Gateway.ChannelPermissions;
using UserStatus = Requestarr.Core.Users.UserStatus;

namespace Requestarr.Core.Test.Imports
{
    // Live: an import of two of Requestrr's replies (made up, in no channel), one film already stuck in the dev stack's
    // TEST Radarr and one already there, with the real bot in the dev server: nothing in the member's channel, one silent
    // card in the feed; then the stuck one is fixed, and its member and the feed are told. Two channels of its own, removed afterwards with the
    // movies and the fake download, pass or fail; the running dev stack isn't touched, so it needn't be stopped.
    //   set -a; . docker/secrets.env; set +a
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveImportAlertsFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveImportAlertsFixture : DbTest
    {
        private const string Container = "requestarr-dev-radarr-test-1";
        private const string ClientName = "Requestarr import check";
        private const string Folder = "/config/requestarr-import-check";
        private const ulong OldChannel = 900000000000100009;
        private const ulong Stranger = 900000000000200009;

        // Old films nobody's test data uses.
        private static readonly (int TmdbId, string Title, int Year) Stuck = (19, "Metropolis", 1927);
        private static readonly (int TmdbId, string Title, int Year) There = (653, "Nosferatu", 1922);

        private DiscordSocketClient _client;
        private IDiscordGuildGateway _gateway;
        private IHttpClient _http;
        private ulong _feedChannel;
        private ulong _memberChannel;
        private bool _isOurs;

        [Test]
        public async Task an_import_alerts_nobody_and_later_changes_do()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token) || string.IsNullOrWhiteSpace(LiveDev.RadarrKey))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN / RADARR_TEST_API_KEY not set");
            }

            UseRealHttp();
            _http = Mocker.Resolve<IHttpClient>();

            try
            {
                await ConnectAsync();
                UseServices();
                var member = await MemberAsync();
                var movies = await AddMoviesAsync();
                await StickAsync();

                await ImportAsync(member);
                var requests = Mocker.Resolve<IRequestRepository>().All().ToDictionary(r => r.TmdbId.Value);
                (requests[Stuck.TmdbId].Imported, requests[Stuck.TmdbId].Progress).Should().Be((true, DownloadProgress.Stalled));
                (requests[There.TmdbId].Imported, requests[There.TmdbId].Status).Should().Be((true, RequestStatus.Available));
                requests[There.TmdbId].OwnerUserId.Should().NotBe(requests[Stuck.TmdbId].OwnerUserId);
                var availability = Mocker.Resolve<AvailabilityService>();

                (await OwnAsync(_memberChannel)).Should().BeEmpty("the import tells nobody what was already so");
                var card = (await OwnAsync(_feedChannel)).Should().ContainSingle().Subject;
                card.Flags.GetValueOrDefault().HasFlag(MessageFlags.SuppressNotification).Should().BeTrue("it's silent");
                card.Embeds.Single().Description.Should().Contain(Stuck.Title).And.NotContain(There.Title);
                Log($"[import] feed: \"{card.Embeds.Single().Title}\": {card.Embeds.Single().Description}");

                await FixAsync(movies[Stuck.TmdbId]);
                await availability.CheckAllAsync();
                var told = (await OwnAsync(_memberChannel)).Should().ContainSingle().Subject;
                told.Content.Should().Contain("ready to watch");
                var after = (await OwnAsync(_feedChannel)).OrderBy(m => m.Id).ToList();
                after.Should().HaveCount(2, "one summary and one card for the change");
                after[1].Embeds.Single().Description.Should().StartWith(DiscordText.FinalReady);
                Log($"[later] member: \"{told.Content}\"; feed: \"{after[1].Embeds.Single().Title}\" {after[1].Embeds.Single().Description.Split('\n')[0]}");
            }
            finally
            {
                await CleanUpAsync();
            }
        }

        // The dev bot's own second connection, with no handlers: the running dev stack's bot answers everything.
        private async Task ConnectAsync()
        {
            var ready = new TaskCompletionSource();
            _client = new DiscordSocketClient(new DiscordSocketConfig { GatewayIntents = GatewayIntents.Guilds });
            _client.Ready += () =>
            {
                ready.TrySetResult();
                return Task.CompletedTask;
            };
            await _client.LoginAsync(TokenType.Bot, LiveDev.Token);
            await _client.StartAsync();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(60));

            var settings = new DiscordSettingsStore();
            settings.Set(LiveDev.Settings());
            var host = new Mock<IDiscordClientHost>();
            host.SetupGet(h => h.Client).Returns(_client);
            host.SetupGet(h => h.State).Returns(DiscordBotState.Connected);
            _gateway = new DiscordNetGateway(host.Object, settings);
            const ChannelPermissions use = ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages | ChannelPermissions.ReadMessageHistory | ChannelPermissions.EmbedLinks;
            var hidden = new[]
            {
                new PermissionOverwrite(LiveDev.GuildId, OverwriteTarget.Role, ChannelPermissions.None, ChannelPermissions.ViewChannel),
                new PermissionOverwrite(_client.CurrentUser.Id, OverwriteTarget.Member, use, ChannelPermissions.None)
            };
            _feedChannel = await _gateway.CreateTextChannelAsync("import-check-feed", null, null, hidden);
            _memberChannel = await _gateway.CreateTextChannelAsync("import-check-member", null, null, hidden);
        }

        private void UseServices()
        {
            Mocker.SetConstant(_gateway);
            Mocker.SetConstant<IBackendRepository>(Mocker.Resolve<BackendRepository>());
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
            Mocker.SetConstant<IRemovalProposalRepository>(Mocker.Resolve<RemovalProposalRepository>());
            Mocker.SetConstant<IRemovalFlagRepository>(Mocker.Resolve<RemovalFlagRepository>());
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            Mocker.SetConstant<IUserIdentityRepository>(Mocker.Resolve<UserIdentityRepository>());
            Mocker.SetConstant<IDiscordGuildConfigRepository>(Mocker.Resolve<DiscordGuildConfigRepository>());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());
            Mocker.SetConstant<IRadarrApi>(Mocker.Resolve<RadarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ApplicationUrl).Returns("https://requests.example");
            Mocker.SetConstant<IPortalLinks>(Mocker.Resolve<PortalLinks>());
            Mocker.Resolve<IDiscordGuildConfigRepository>().Insert(new DiscordGuildConfig { GuildId = LiveDev.GuildId.ToString(), FeedChannelId = _feedChannel.ToString() });
            Mocker.SetConstant<IRequestFeed>(Mocker.Resolve<RequestFeed>());
            Mocker.SetConstant<IImportSummary>(Mocker.Resolve<ImportSummary>());
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IImportedMessageRepository>(Mocker.Resolve<ImportedMessageRepository>());
            Mocker.SetConstant<IUserManagementService>(Mocker.Resolve<UserManagementService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            Mocker.SetConstant<IAvailabilityService>(Mocker.Resolve<AvailabilityService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = LiveDev.RadarrUrl, ApiKey = LiveDev.RadarrKey, DefaultAccess = "approval", QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } }
            });
        }

        // The test member, onboarded with this check's channel as theirs; their own server roles and channels are untouched.
        private async Task<RequestarrUser> MemberAsync()
        {
            var members = await (await _client.Rest.GetGuildAsync(LiveDev.GuildId)).GetUsersAsync().FlattenAsync();
            var target = LiveTestMembers.Pick(members)[0];
            var user = Mocker.Resolve<IRequestarrUserRepository>().Insert(new RequestarrUser { DisplayName = target.Username, Status = UserStatus.Active, PrivateChannelId = _memberChannel.ToString(), Added = DateTime.UtcNow });
            Mocker.Resolve<IUserIdentityRepository>().Insert(new UserIdentity { UserId = user.Id, Provider = UserIdentity.DiscordProvider, ExternalId = target.Id.ToString(), Username = target.Username, Linked = DateTime.UtcNow });

            return user;
        }

        // The whole import, as the Import page runs it: preview, confirm, write, first check. The test member asked for the
        // stuck film, someone never onboarded for the other.
        private async Task ImportAsync(RequestarrUser member)
        {
            var memberId = ulong.Parse(Mocker.Resolve<IUserIdentityRepository>().GetForUser(member.Id).Single().ExternalId);
            var messages = new[] { Sent(2, Stuck, memberId, member.DisplayName), Sent(1, There, Stranger, "Not onboarded") };
            Mocker.GetMock<IChannelHistory>().Setup(h => h.DescribeAsync(OldChannel)).ReturnsAsync(new ImportChannel(OldChannel, "Old server", "media-requests", true, null));
            Mocker.GetMock<IChannelHistory>().Setup(h => h.ReadAsync(OldChannel, It.IsAny<ulong?>())).ReturnsAsync((ulong _, ulong? before) => messages.Where(m => before == null || m.Id < before).ToList());
            var import = Mocker.Resolve<ImportService>();
            Mocker.GetMock<IManageCommandQueue>()
                .Setup(q => q.Push(It.IsAny<ImportRequestrrCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                .Callback<ImportRequestrrCommand, CommandPriority, CommandTrigger>((command, _, _) => import.Execute(command));

            var job = await import.StartPreviewAsync(new[] { OldChannel });
            await UntilAsync("the import's preview is ready", () => Task.FromResult(import.Job(job.Id).State != ImportJobState.Reading));
            import.Job(job.Id).Problem.Should().BeNull();
            (await import.ConfirmAsync(job.Id, Actor.Web("automated check"))).Result.Requests.Should().Be(2);
            Log("[import] imported 2 requests from two of Requestrr's replies");
        }

        // Requestrr's reply to someone's request for a film.
        private static HistoryMessage Sent(ulong id, (int TmdbId, string Title, int Year) film, ulong who, string name)
        {
            var text = $"Your request for **{film.Title}** was sent successfully!";
            var card = new HistoryEmbed($"{film.Title} ({film.Year})", $"https://www.themoviedb.org/movie/{film.TmdbId}");

            return new HistoryMessage(id, OldChannel, DateTime.UtcNow.AddYears(-1), 900000000000300001, true, text, new[] { card }, who, name, Array.Empty<ulong>(), 0);
        }

        private async Task<Dictionary<int, int>> AddMoviesAsync()
        {
            var movies = (JArray)await SendAsync(HttpMethod.Get, "movie");
            movies.Children<JObject>().Select(m => (int)m["tmdbId"]).Should().NotContain(new[] { Stuck.TmdbId, There.TmdbId }, "this check's films must not be anyone else's");
            _isOurs = true;
            var profile = (int)((JArray)await SendAsync(HttpMethod.Get, "qualityprofile"))[0]["id"];
            var added = new Dictionary<int, int>();

            foreach (var film in new[] { Stuck, There })
            {
                var movie = (JObject)await SendAsync(HttpMethod.Get, $"movie/lookup/tmdb?tmdbId={film.TmdbId}");
                movie["qualityProfileId"] = profile;
                movie["rootFolderPath"] = "/data/movies";
                movie["monitored"] = true;
                added[film.TmdbId] = (int)(await SendAsync(HttpMethod.Post, "movie", movie))["id"];
            }

            await PlaceAsync(added[There.TmdbId], There);

            return added;
        }

        // A finished download with nothing in it to import: Radarr waits for someone to deal with it.
        private async Task StickAsync()
        {
            var release = $"{Folder}/watch/{Stuck.Title}.{Stuck.Year}.1080p.BluRay.x264-TEST";
            Docker($"mkdir -p {Folder}/nzb {release} && echo nothing > {release}/readme.txt && chown -R abc:users {Folder}");
            var client = new JObject
            {
                ["name"] = ClientName, ["enable"] = true, ["protocol"] = "usenet", ["implementation"] = "UsenetBlackhole", ["configContract"] = "UsenetBlackholeSettings",
                ["priority"] = 1, ["removeCompletedDownloads"] = false, ["removeFailedDownloads"] = false, ["tags"] = new JArray(),
                ["fields"] = new JArray(new JObject { ["name"] = "nzbFolder", ["value"] = $"{Folder}/nzb" }, new JObject { ["name"] = "watchFolder", ["value"] = $"{Folder}/watch" })
            };
            await SendAsync(HttpMethod.Post, "downloadclient", client);

            await UntilAsync("the download is stuck", async () =>
            {
                await SendAsync(HttpMethod.Post, "command", new JObject { ["name"] = "RefreshMonitoredDownloads" });
                var queue = (await SendAsync(HttpMethod.Get, "queue?includeUnknownMovieItems=false"))["records"].Children<JObject>();
                return queue.Any(q => (string)q["trackedDownloadStatus"] == "warning");
            });
        }

        private async Task FixAsync(int movieId)
        {
            Docker($"rm -rf {Folder}/watch/*");
            await PlaceAsync(movieId, Stuck);
            await UntilAsync("the queue is empty", async () =>
            {
                await SendAsync(HttpMethod.Post, "command", new JObject { ["name"] = "RefreshMonitoredDownloads" });
                return (await SendAsync(HttpMethod.Get, "queue"))["records"].Children<JObject>().All(q => (int?)q["movieId"] != movieId);
            });
        }

        private async Task PlaceAsync(int movieId, (int TmdbId, string Title, int Year) film)
        {
            var path = (string)(await SendAsync(HttpMethod.Get, $"movie/{movieId}"))["path"];
            Docker($"mkdir -p '{path}' && truncate -s 1500M '{path}/{film.Title}.{film.Year}.1080p.BluRay.x264-TEST.mkv' && chown -R abc:users '{path}'");
            await SendAsync(HttpMethod.Post, "command", new JObject { ["name"] = "RescanMovie", ["movieId"] = movieId });
            await UntilAsync($"{film.Title} is there", async () => (bool)(await SendAsync(HttpMethod.Get, $"movie/{movieId}"))["hasFile"]);
        }

        private static async Task UntilAsync(string what, Func<Task<bool>> isDone)
        {
            for (var i = 0; i < 45; i++)
            {
                if (await isDone())
                {
                    return;
                }

                await Task.Delay(2000);
            }

            Assert.Fail($"After 90 seconds it still isn't so that {what}");
        }

        private async Task<List<IMessage>> OwnAsync(ulong channel) =>
            (await ((IMessageChannel)await _client.Rest.GetChannelAsync(channel)).GetMessagesAsync(20).FlattenAsync()).Where(m => m.Author.Id == _client.CurrentUser.Id).ToList();

        // Only what this check made.
        private async Task CleanUpAsync()
        {
            try
            {
                await CleanUpRadarrAsync();
            }
            finally
            {
                foreach (var channel in new[] { _feedChannel, _memberChannel }.Where(c => c != 0))
                {
                    await _gateway.DeleteChannelAsync(channel);
                }

                if (_client != null)
                {
                    await _client.StopAsync();
                    await _client.DisposeAsync();
                }
            }

            Log("[cleanup] its channels, movies and fake download are gone");
        }

        private async Task CleanUpRadarrAsync()
        {
            if (_isOurs)
            {
                foreach (var client in (await SendAsync(HttpMethod.Get, "downloadclient")).Children<JObject>().Where(c => (string)c["name"] == ClientName))
                {
                    await SendAsync(HttpMethod.Delete, $"downloadclient/{(int)client["id"]}");
                }

                foreach (var movie in (await SendAsync(HttpMethod.Get, "movie")).Children<JObject>().Where(m => (int)m["tmdbId"] == Stuck.TmdbId || (int)m["tmdbId"] == There.TmdbId))
                {
                    await SendAsync(HttpMethod.Delete, $"movie/{(int)movie["id"]}?deleteFiles=true");
                }

                Docker($"rm -rf {Folder}");
            }
        }

        private async Task<JToken> SendAsync(HttpMethod method, string resource, JObject body = null)
        {
            var request = new HttpRequestBuilder($"{LiveDev.RadarrUrl}/api/v3/{resource}").SetHeader("X-Api-Key", LiveDev.RadarrKey).Build();
            request.Method = method;

            if (body != null)
            {
                request.Headers.ContentType = "application/json";
                request.SetContent(body.ToString());
            }

            var content = (await _http.ExecuteAsync(request)).Content;

            return string.IsNullOrWhiteSpace(content) ? new JObject() : JToken.Parse(content);
        }

        private static void Docker(string script)
        {
            var start = new ProcessStartInfo("docker") { RedirectStandardError = true };

            foreach (var arg in new[] { "exec", Container, "sh", "-c", script })
            {
                start.ArgumentList.Add(arg);
            }

            using var process = Process.Start(start);
            process.WaitForExit(60000);
            process.ExitCode.Should().Be(0, process.StandardError.ReadToEnd());
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
