using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Metadata;
using Requestarr.Core.Portal;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;
using Requestarr.Test.Common.Categories;

namespace Requestarr.Core.Test.Portal
{
    // Live: the admin portal's actions with the real bot in the dev server and the dev stack's test Radarr.
    // The test member asks to join and is approved from the portal; asks for two films, one denied and one approved
    // from the portal; is suspended and unsuspended; has a removal started and dropped on the approved film. Each
    // Discord result is read back: the access request card, the member's request messages, their channel's notices,
    // the removal prompt. The portal acts as "qa-checks (web app)". The member stays, active, for the portal's pages.
    // Never runs in CI (ManualTest). Needs the stack's Requestarr stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LivePortalActionsFixture : DbTest
    {
        // Films no other live check requests.
        private const int PulpFiction = 680;
        private const int Goodfellas = 769;

        private static readonly Actor Admin = Actor.Web("qa-checks");

        [Test]
        public async Task portal_actions_show_in_discord()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token) || string.IsNullOrWhiteSpace(LiveDev.RadarrKey))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN / RADARR_TEST_API_KEY not set");
            }

            var live = Start();

            try
            {
                await live.Host.StartAsync(live.Settings.Current);
                await live.Host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
                await Run(live);
            }
            finally
            {
                if (live.Host.Client?.GetGuild(live.Settings.Current.GuildId) is { } guild)
                {
                    await guild.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await live.Host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-dev to restore the dev stack)");
            }
        }

        private async Task Run(Live live)
        {
            var guild = live.Host.Client.GetGuild(live.Settings.Current.GuildId);
            var member = LiveTestMembers.Pick(await (await live.Host.Client.Rest.GetGuildAsync(guild.Id)).GetUsersAsync().FlattenAsync()).First();
            var portal = Mocker.Resolve<PortalActions>();
            var people = Mocker.Resolve<PortalMemberActions>();
            Log($"[member] test member: {member.Username}");

            // 1. Access requests: they ask from the welcome channel; the portal approves; the admin channel's card closes.
            await Mocker.Resolve<IAccessRequestService>().SubmitAsync(member.Id, member.Username, member.Username, new[] { ContentType.Movie }, "live portal check", null, null);
            var asked = Mocker.Resolve<IAccessRequestRepository>().Pending().Single();
            var approved = await people.ApproveAccessAsync(asked.Id, new Dictionary<ContentType, AccessState> { [ContentType.Movie] = AccessState.NeedsApproval }, Admin);
            var card = await Read(live, live.AdminChannel, ulong.Parse(Mocker.Resolve<IAccessRequestRepository>().Get(asked.Id).CardMessageId));
            Log($"[access] portal: {approved.Message} | the admin channel's card, read back: \"{card}\"");
            approved.Success.Should().BeTrue();
            card.Should().Contain("qa-checks (web app)");

            var user = live.Users.FindByDiscordId(member.Id);
            var channel = ulong.Parse(user.PrivateChannelId);

            // 2. Requests: two films asked for; the portal denies one and approves the other; their messages say so.
            await RequestInRealMessage(live, guild.Id, channel, member.Id, PulpFiction);
            await RequestInRealMessage(live, guild.Id, channel, member.Id, Goodfellas);
            var requests = Mocker.Resolve<IRequestRepository>();
            var pulp = requests.All().Single(r => r.TmdbId == PulpFiction);
            var goodfellas = requests.All().Single(r => r.TmdbId == Goodfellas);

            var denied = await portal.DenyAsync(pulp.Id, "live portal check", Admin);
            var approvedFilm = await portal.ApproveAsync(goodfellas.Id, Admin);
            var deniedCard = await Read(live, live.AdminChannel, ulong.Parse(requests.Get(pulp.Id).AdminMessageId));
            var deniedMessage = await Read(live, channel, ulong.Parse(requests.Get(pulp.Id).MessageId));
            var approvedMessage = await Read(live, channel, ulong.Parse(requests.Get(goodfellas.Id).MessageId));
            Log($"[requests] deny: {denied.Message} | approval card, read back: \"{deniedCard}\" | their message: \"{deniedMessage}\"");
            Log($"[requests] approve: {approvedFilm.Message} | their message, read back: \"{approvedMessage}\"");
            deniedCard.Should().Contain("qa-checks (web app)");
            deniedMessage.Should().Contain("live portal check");
            requests.Get(goodfellas.Id).Status.Should().Be(RequestStatus.Approved);

            // 3. Members: suspended and unsuspended from the portal; their channel says so each time.
            var suspended = await people.SuspendAsync(user.Id, Admin);
            var whileSuspended = await Latest(live, channel, "suspended");
            var unsuspended = await people.UnsuspendAsync(user.Id, Admin);
            var afterwards = await Latest(live, channel, "restored");
            Log($"[member] suspend: {suspended.Message} | their channel, read back: \"{whileSuspended}\"");
            Log($"[member] unsuspend: {unsuspended.Message} | their channel, read back: \"{afterwards}\"");
            suspended.Success.Should().BeTrue();
            whileSuspended.Should().Contain("suspended");
            unsuspended.Success.Should().BeTrue();
            live.Users.FindByDiscordId(member.Id).Status.Should().Be(Requestarr.Core.Users.UserStatus.Active);

            // 4. Removals: started on the approved film (its requester is asked first), then dropped.
            var started = await portal.StartRemovalAsync(goodfellas.Id, new List<int>(), false, Admin);
            var prompt = await Latest(live, channel, "GoodFellas");
            Log($"[removal] start: {started.Message} | the requester's channel, read back: \"{prompt}\"");
            var removal = Mocker.Resolve<IRemovalProposalRepository>().ForRequest(goodfellas.Id).Single();
            var dropped = await portal.DropRemovalAsync(removal.Id, Admin);
            Log($"[removal] drop: {dropped.Message} | status {Mocker.Resolve<IRemovalProposalRepository>().Get(removal.Id).Status}, closed by {Mocker.Resolve<IRemovalProposalRepository>().Get(removal.Id).ClosedByName}");
            started.Success.Should().BeTrue();
            prompt.Should().Contain("GoodFellas");
            dropped.Success.Should().BeTrue();
            Mocker.Resolve<IRemovalProposalRepository>().Get(removal.Id).ClosedByName.Should().Be("qa-checks (web app)");
        }

        private sealed record Live(DiscordSettingsStore Settings, DiscordClientHost Host, IUserManagementService Users, IDiscordGuildGateway Gateway, InteractionDispatcher Dispatcher, IGuildSetupService GuildSetup)
        {
            public ulong AdminChannel => ulong.Parse(GuildSetup.GetConfig().AdminChannelId);
        }

        // The services as the app has them, over a real database and the real bot; as LiveAdminRequestsFixture builds them.
        private Live Start()
        {
            UseRealHttp();
            var settings = new DiscordSettingsStore();
            settings.Set(LiveDev.Settings());
            Mocker.SetConstant<IDiscordSettingsStore>(settings);
            UseRealRepositories();
            Mocker.SetConstant<IRadarrApi>(Mocker.Resolve<RadarrApi>());
            Mocker.SetConstant<ISonarrApi>(Mocker.Resolve<SonarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = LiveDev.RadarrUrl, ApiKey = LiveDev.RadarrKey, QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } }
            });
            Mocker.Resolve<IBackendService>().CheckAllAsync().GetAwaiter().GetResult();
            Mocker.SetConstant<ITmdbSettingsStore>(new TmdbSettingsStore());
            Mocker.SetConstant<IMetadataService>(Mocker.Resolve<MetadataService>());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());
            Mocker.SetConstant<IMediaServerConnections>(new FakeMediaServers());

            IGuildSetupService guildSetup = null;
            IUserManagementService users = null;
            IPanelService panels = null;
            IRequestFeed feed = null;
            IAccessRequestService access = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => feed),
                new Lazy<IAccessRequestService>(() => access),
                new Lazy<Requestarr.Core.Settings.IDiscordServerChoice>(() => null),
                new Lazy<IDiscordAdmins>(() => null),
                Mocker.Resolve<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));
            var gateway = new DiscordNetGateway(host, settings);
            Mocker.SetConstant<IDiscordClientHost>(host);
            Mocker.SetConstant<IDiscordGuildGateway>(gateway);
            guildSetup = Mocker.Resolve<GuildSetupService>();
            Mocker.SetConstant(guildSetup);
            feed = Mocker.Resolve<RequestFeed>();
            Mocker.SetConstant(feed);
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            Mocker.SetConstant<IAdminQueue>(new AdminQueue(new IAdminQueueSource[] { Mocker.Resolve<PendingRequestsSource>(), Mocker.Resolve<TroubleSource>() }));
            Mocker.SetConstant<IConnectionMonitor>(Mocker.Resolve<ConnectionMonitor>());
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IAnimeService>(Mocker.Resolve<AnimeService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            Mocker.SetConstant<IRemovalService>(Mocker.Resolve<RemovalService>());
            access = Mocker.Resolve<AccessRequestService>();
            Mocker.SetConstant(access);

            var handlers = new IInteractionHandler[] { Mocker.Resolve<RequestHandler>(), Mocker.Resolve<ApprovalHandler>(), Mocker.Resolve<PanelHandler>() };

            return new Live(settings, host, users, gateway, new InteractionDispatcher(handlers, LogManager.GetLogger("LiveDiscord")), guildSetup);
        }

        private void UseRealRepositories()
        {
            Mocker.SetConstant<IRequestarrUserRepository>(Mocker.Resolve<RequestarrUserRepository>());
            Mocker.SetConstant<IUserIdentityRepository>(Mocker.Resolve<UserIdentityRepository>());
            Mocker.SetConstant<IUserMediaAccessRepository>(Mocker.Resolve<UserMediaAccessRepository>());
            Mocker.SetConstant<IMediaServerAccountRepository>(Mocker.Resolve<MediaServerAccountRepository>());
            Mocker.SetConstant<IMediaServerAccessRequestRepository>(Mocker.Resolve<MediaServerAccessRequestRepository>());
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IDiscordGuildConfigRepository>(Mocker.Resolve<DiscordGuildConfigRepository>());
            Mocker.SetConstant<IBackendRepository>(Mocker.Resolve<BackendRepository>());
            Mocker.SetConstant<IRequestRepository>(Mocker.Resolve<RequestRepository>());
            Mocker.SetConstant<IRequestSubscriberRepository>(Mocker.Resolve<RequestSubscriberRepository>());
            Mocker.SetConstant<IRemovalProposalRepository>(Mocker.Resolve<RemovalProposalRepository>());
            Mocker.SetConstant<IRemovalFlagRepository>(Mocker.Resolve<RemovalFlagRepository>());
            Mocker.SetConstant<IRemovalPromptRepository>(Mocker.Resolve<RemovalPromptRepository>());
            Mocker.SetConstant<IOutageRepository>(Mocker.Resolve<OutageRepository>());
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());
            Mocker.SetConstant<IWelcomeGuardRepository>(Mocker.Resolve<WelcomeGuardRepository>());
        }

        // Posts the request's message as the bot (as /request leaves it), then presses Request on it.
        private static async Task RequestInRealMessage(Live live, ulong guild, ulong channel, ulong user, int tmdbId)
        {
            var message = await live.Gateway.SendMessageAsync(channel, new InteractionReply { Content = $"🎬 **Movies request** · details (tmdb:{tmdbId})" });
            var responder = new RecordingResponder(null, channel, message, false);
            await live.Dispatcher.DispatchAsync(FakeInteraction.Button($"req-make:movie:{tmdbId}").By(user).InGuild(guild).InChannel(channel).OnMessage(message).Build(), responder);
            await live.Gateway.EditMessageAsync(channel, message, responder.LastReply);
            Log($"[request] tmdb:{tmdbId} -> {responder.LastReply.Content.Replace("\n", " | ")}");
        }

        // A message's words as Discord shows them: its text, or its card's title and description.
        private static async Task<string> Read(Live live, ulong channel, ulong message)
        {
            var read = await ((IMessageChannel)await live.Host.Client.Rest.GetChannelAsync(channel)).GetMessageAsync(message);
            return Words(read);
        }

        // The bot's newest message in the channel that mentions the words (the member's panel moves below the rest).
        private static async Task<string> Latest(Live live, ulong channel, string mentioning)
        {
            var messages = await ((IMessageChannel)await live.Host.Client.Rest.GetChannelAsync(channel)).GetMessagesAsync(10).FlattenAsync();
            return messages.Where(m => m.Author.Id == live.Host.Client.CurrentUser.Id).Select(Words).FirstOrDefault(w => w.Contains(mentioning, StringComparison.OrdinalIgnoreCase)) ?? string.Empty;
        }

        private static string Words(IMessage message)
        {
            var embed = message?.Embeds.FirstOrDefault();
            var text = $"{message?.Content} {embed?.Title} {embed?.Description}".Trim();

            foreach (var container in message?.Components.OfType<ContainerComponent>() ?? Enumerable.Empty<ContainerComponent>())
            {
                text += " " + string.Join(" ", container.Components.OfType<TextDisplayComponent>().Select(t => t.Content));
            }

            return text.Replace("\n", " | ");
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
