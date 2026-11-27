using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.WebSocket;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Messaging.Commands;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Handlers;
using Requestarr.Core.Discord.Interactions;
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
    // Live: a real import from the original server, read through the dev bot's import-only connection to it, with Post in
    // the admin feed on, then the test member's history in their channel. The smallest of its channels Requestrr answered
    // in is imported (all of them would post hundreds of cards). The test member is onboarded with their history left out
    // (Active with their own channel, as onboarding leaves them); when they made no request there, a few of the imported
    // requests are given to them. They type /history twice, and an admin /admin history, as interactions through the test
    // harness (a user account is never automated). Read back from Discord: one silent card per old request in their
    // channel, oldest first, once; the feed's header, Imported cards and closing post. Nobody else is onboarded or
    // messaged. Its two channels are hidden and removed afterwards, pass or fail; the original server is only read, and
    // the running dev stack isn't touched.
    //   set -a; . docker/secrets.env; set +a
    //   dotnet test Requestarr.Core.Test.dll --filter "FullyQualifiedName~LiveImportHistoryFixture" --logger "console;verbosity=detailed"
    [TestFixture]
    [ManualTest]
    public class LiveImportHistoryFixture : DbTest
    {
        // The requests given to the test member when the original server has none of theirs.
        private const int Given = 3;

        // Between two of the history's posts (production waits 2 seconds; Discord paces the rest).
        private static readonly TimeSpan Pace = TimeSpan.FromMilliseconds(500);

        private DiscordSocketClient _client;
        private IDiscordGuildGateway _gateway;
        private HistoryPosting _posting;
        private InteractionDispatcher _dispatcher;
        private ulong _feedChannel;
        private ulong _memberChannel;

        [Test]
        public async Task the_original_servers_history_is_posted_once_and_silently()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN not set");
            }

            UseRealHttp();

            try
            {
                await ConnectAsync();
                UseServices();
                var imported = await ImportSmallestChannelAsync();
                await FeedAsync(imported);
                var (member, discordId) = await TestMemberAsync(imported);
                await HistoryAsync(member, discordId);
                await AdminHistoryAsync();
                NobodyElse(member);
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

                Log("[cleanup] its two channels and their posts are gone; the original server was only read");
            }
        }

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
            Mocker.SetConstant<IDiscordSettingsStore>(settings);
            var host = new Mock<IDiscordClientHost>();
            host.SetupGet(h => h.Client).Returns(_client);
            host.SetupGet(h => h.State).Returns(DiscordBotState.Connected);
            Mocker.SetConstant(host.Object);
            _gateway = new DiscordNetGateway(host.Object, settings);
            const ChannelPermissions use = ChannelPermissions.ViewChannel | ChannelPermissions.SendMessages | ChannelPermissions.ReadMessageHistory | ChannelPermissions.EmbedLinks;
            var hidden = new[]
            {
                new PermissionOverwrite(LiveDev.GuildId, OverwriteTarget.Role, ChannelPermissions.None, ChannelPermissions.ViewChannel),
                new PermissionOverwrite(_client.CurrentUser.Id, OverwriteTarget.Member, use, ChannelPermissions.None)
            };
            _feedChannel = await _gateway.CreateTextChannelAsync("history-check-feed", null, null, hidden);
            _memberChannel = await _gateway.CreateTextChannelAsync("history-check-member", null, null, hidden);
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
            var config = Mocker.Resolve<IDiscordGuildConfigRepository>().Insert(new DiscordGuildConfig { GuildId = LiveDev.GuildId.ToString(), FeedChannelId = _feedChannel.ToString(), AdminChannelId = _feedChannel.ToString() });
            Mocker.GetMock<IGuildSetupService>().Setup(g => g.GetConfig()).Returns(config);
            Mocker.SetConstant<IAuditRepository>(Mocker.Resolve<AuditRepository>());
            Mocker.SetConstant<IAuditService>(Mocker.Resolve<AuditService>());
            Mocker.SetConstant<IImportedMessageRepository>(Mocker.Resolve<ImportedMessageRepository>());
            Mocker.SetConstant<IHistoryPostRepository>(Mocker.Resolve<HistoryPostRepository>());
            Mocker.SetConstant<IAvailabilitySettingsStore>(new AvailabilitySettingsStore());
            Mocker.SetConstant<IRadarrApi>(Mocker.Resolve<RadarrApi>());
            Mocker.SetConstant<ISonarrApi>(Mocker.Resolve<SonarrApi>());
            Mocker.SetConstant<IBackendService>(Mocker.Resolve<BackendService>());
            Mocker.GetMock<IConfigService>().SetupGet(c => c.ApplicationUrl).Returns("https://requests.example");
            Mocker.SetConstant<IPortalLinks>(Mocker.Resolve<PortalLinks>());
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr (test)", Url = LiveDev.RadarrUrl, ApiKey = LiveDev.RadarrKey, DefaultAccess = "approval", QualityProfile = "HD-1080p", RootFolder = "/data/movies", Default = true } },
                Sonarr = new List<BackendInstanceProvisioning> { new() { Name = "Sonarr (test)", Url = LiveDev.SonarrUrl, ApiKey = LiveDev.SonarrKey, DefaultAccess = "approval", QualityProfile = "HD-1080p", RootFolder = "/data/tv", SeriesType = "standard", Purpose = "everything" } }
            });
            Mocker.SetConstant<IRequestFeed>(Mocker.Resolve<RequestFeed>());
            Mocker.SetConstant<IImportSummary>(Mocker.Resolve<ImportSummary>());
            Mocker.SetConstant<IChannelHistory>(Mocker.Resolve<ChannelHistory>());

            // Post in the admin feed on, as the owner's import has it; posting when onboarding off (history left out).
            Mocker.GetMock<IHistorySettings>().SetupGet(s => s.InFeed).Returns(true);
            Mocker.GetMock<IHistorySettings>().SetupGet(s => s.WhenOnboarding).Returns(false);
            _posting = Mocker.Resolve<HistoryPosting>();
            _posting.Wait = _ => Task.Delay(Pace);
            Mocker.SetConstant<IHistoryPosting>(_posting);
            Mocker.SetConstant<IUserManagementService>(Mocker.Resolve<UserManagementService>());
            Mocker.SetConstant<IRequestService>(Mocker.Resolve<RequestService>());
            Mocker.SetConstant<IAvailabilityService>(Mocker.Resolve<AvailabilityService>());
            Mocker.SetConstant<ICallerResolver>(Mocker.Resolve<CallerResolver>());
            _dispatcher = new InteractionDispatcher(new IInteractionHandler[] { Mocker.Resolve<HistoryHandler>() }, NLog.LogManager.GetLogger("LiveDiscord"));
        }

        // Every channel of the original server Requestrr answered in, each previewed, and the one with the fewest requests
        // imported: its new requests, oldest first.
        private async Task<IReadOnlyList<Request>> ImportSmallestChannelAsync()
        {
            var original = _client.Guilds.Where(g => g.Id != LiveDev.GuildId).Should().ContainSingle("the dev bot is in the original server, import only").Subject;
            var import = Mocker.Resolve<ImportService>();
            Mocker.GetMock<IManageCommandQueue>()
                .Setup(q => q.Push(It.IsAny<ImportRequestrrCommand>(), It.IsAny<CommandPriority>(), It.IsAny<CommandTrigger>()))
                .Callback<ImportRequestrrCommand, CommandPriority, CommandTrigger>((command, _, _) => import.Execute(command));

            var channels = (await import.ReadableChannelsAsync()).Where(c => c.ServerId == original.Id.ToString() && c.HasSourceReplies).ToList();
            Log($"[import] the original server: {channels.Count} channels Requestrr answered in");
            channels.Should().NotBeEmpty();
            var previews = new List<(ImportChannel Channel, ImportJobView Job)>();

            foreach (var channel in channels)
            {
                var job = await import.StartPreviewAsync(new[] { channel.Id });
                await UntilAsync($"#{channel.Name} is read", () => Task.FromResult(import.Job(job.Id).State != ImportJobState.Reading));
                var read = import.Job(job.Id);
                previews.Add((channel, read));
                Log($"[import] #{channel.Name}: {read.Plan?.Requests.Count ?? 0} requests from {read.Plan?.MessagesRead ?? 0} messages read ({read.Plan?.NotRequests ?? 0} not requests){(read.Problem != null ? "; problem: " + read.Problem : string.Empty)}{(read.Warning != null ? "; warning: " + read.Warning : string.Empty)}");
            }

            var (smallest, chosen) = previews.Where(p => p.Job.Plan?.Requests.Count > 0).OrderBy(p => p.Job.Plan.Requests.Count).First();
            var result = (await import.ConfirmAsync(chosen.Id, Actor.Web("automated check"))).Result;
            Log($"[import] imported #{smallest.Name}: {result.Requests} requests, {result.People} people");
            result.Requests.Should().BePositive();

            return Mocker.Resolve<IRequestRepository>().All().Where(r => r.Imported).OrderBy(r => r.Requested).ToList();
        }

        // The import's history in the admin feed: a header, an Imported card for each request, and a closing post, all
        // silent, with no mentions.
        private async Task FeedAsync(IReadOnlyList<Request> imported)
        {
            await PostedAllAsync("the feed's history is posted");
            var feed = await OwnAsync(_feedChannel);
            var cards = feed.Where(IsImportedCard).ToList();
            Log($"[feed] {feed.Count} posts: header \"{Words(feed[0])}\"; {cards.Count} Imported cards, the first \"{Words(cards[0])}\"; closing \"{Words(feed[^1])}\"");
            cards.Should().HaveCountGreaterThanOrEqualTo(imported.Count, "a card for each imported request");
            IsImportedCard(feed[0]).Should().BeFalse("a header comes first");
            IsImportedCard(feed[^1]).Should().BeFalse("a closing post comes last");
            feed.Should().OnlyContain(m => m.Flags.GetValueOrDefault().HasFlag(MessageFlags.SuppressNotification) && !m.MentionedUserIds.Any() && !m.MentionedRoleIds.Any() && !m.MentionedEveryone, "nobody gets a ping or a notification");
        }

        private static bool IsImportedCard(IMessage message) => message.Embeds.FirstOrDefault()?.Description?.Contains("Imported") == true;

        // A post's words as its reader sees them first: its text, or its card's title and first line.
        private static string Words(IMessage message) =>
            $"{message.Content}{(message.Embeds.FirstOrDefault() is { } card ? $" {card.Title} {card.Description?.Split('\n')[0]}" : string.Empty)}".Trim();

        // The test member, onboarded with their history left out: Active, with their own channel. Their requests from the
        // original server, or, when they made none there, a few of the imported ones given to them.
        private async Task<(RequestarrUser Member, ulong DiscordId)> TestMemberAsync(IReadOnlyList<Request> imported)
        {
            var members = await (await _client.Rest.GetGuildAsync(LiveDev.GuildId)).GetUsersAsync().FlattenAsync();
            var target = LiveTestMembers.Pick(members)[0];
            var users = Mocker.Resolve<IRequestarrUserRepository>();
            var identities = Mocker.Resolve<IUserIdentityRepository>();
            var identity = identities.All().FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider && i.ExternalId == target.Id.ToString());
            var member = identity != null ? users.Get(identity.UserId) : users.Insert(new RequestarrUser { DisplayName = target.Username, Added = DateTime.UtcNow });

            if (identity == null)
            {
                identities.Insert(new UserIdentity { UserId = member.Id, Provider = UserIdentity.DiscordProvider, ExternalId = target.Id.ToString(), Username = target.Username, Linked = DateTime.UtcNow });
            }

            member.Status = UserStatus.Active;
            member.PrivateChannelId = _memberChannel.ToString();
            users.Update(member);

            var theirs = imported.Where(r => r.OwnerUserId == member.Id).ToList();

            if (theirs.Count == 0)
            {
                theirs = imported.Take(Given).ToList();
                theirs.ForEach(r => r.OwnerUserId = member.Id);
                Mocker.Resolve<IRequestRepository>().UpdateMany(theirs);
                Log($"[member] {target.Username} made no request in the original server: {theirs.Count} of the imported requests are given to them for the check");
            }
            else
            {
                Log($"[member] {target.Username} made {theirs.Count} requests in the original server");
            }

            (await OwnAsync(_memberChannel)).Should().BeEmpty("onboarded with their history left out");
            return (member, target.Id);
        }

        // /history twice in their channel: a card for each of their old requests, oldest first, posted once; then nothing.
        private async Task HistoryAsync(RequestarrUser member, ulong discordId)
        {
            var theirs = Mocker.Resolve<IRequestRepository>().All().Where(r => r.Imported && r.OwnerUserId == member.Id).OrderBy(r => r.Requested).ToList();
            var first = await SendAsync(FakeInteraction.SlashCommand("history").By(discordId).InGuild(LiveDev.GuildId).InChannel(_memberChannel));
            Log($"[history] /history: \"{first}\"");
            await PostedAllAsync("their history is posted");
            var cards = (await OwnAsync(_memberChannel)).ToList();

            foreach (var (card, request) in cards.Zip(theirs))
            {
                var embed = card.Embeds.Single();
                Log($"[history] \"{card.Content}\" | {embed.Title} | {embed.Description?.Replace("\n", " · ")} | {embed.Footer?.Text} | poster {(embed.Thumbnail.HasValue ? "shown" : "none")} | silent {card.Flags.GetValueOrDefault().HasFlag(MessageFlags.SuppressNotification)}");
                embed.Title.Should().Contain(request.Title, "oldest first, each its own request");
                embed.Description.Should().Contain("Imported").And.Contain(DiscordText.FeedState(request), "the card says how it stands");
                embed.Footer?.Text.Should().Contain($"#{request.Id}", "the card's footer has the request's number");

                if (request.PosterUrl != null)
                {
                    embed.Thumbnail.HasValue.Should().BeTrue($"{request.Title} has a poster");
                }

                if (request.ContentType == ContentType.Tv)
                {
                    embed.Description.Should().Contain("**Seasons:**");
                }
            }

            cards.Should().HaveCount(theirs.Count, "one card for each of their old requests");
            cards.Should().OnlyContain(m => m.Flags.GetValueOrDefault().HasFlag(MessageFlags.SuppressNotification) && !m.MentionedUserIds.Any() && !m.MentionedEveryone, "silent, with no mentions");

            var again = await SendAsync(FakeInteraction.SlashCommand("history").By(discordId).InGuild(LiveDev.GuildId).InChannel(_memberChannel));
            Log($"[history] /history again: \"{again}\"");
            await Task.Delay(TimeSpan.FromSeconds(5));
            (await OwnAsync(_memberChannel)).Should().HaveCount(theirs.Count, "the second /history posts nothing");
        }

        // /admin history from the server's owner, in the admin channel (the check's hidden feed channel stands in for it):
        // what it posts, or that it's all posted.
        private async Task AdminHistoryAsync()
        {
            var owner = (await _client.Rest.GetGuildAsync(LiveDev.GuildId)).OwnerId;
            var said = await SendAsync(FakeInteraction.SlashCommand("admin history").By(owner, "owner").InGuild(LiveDev.GuildId).InChannel(_feedChannel));
            Log($"[admin history] \"{said}\"");
            said.Should().NotBeNullOrEmpty().And.NotContain("only work", "it's answered in the admin channel");
            await PostedAllAsync("anything /admin history queued is posted");
        }

        // Everyone else the import found stays imported, with no channel: nobody else is onboarded or messaged.
        private void NobodyElse(RequestarrUser member)
        {
            var others = Mocker.Resolve<IRequestarrUserRepository>().All().Where(u => u.Id != member.Id).ToList();
            Log($"[others] {others.Count} other people known from the import: {string.Join(", ", others.GroupBy(u => u.Status).Select(g => $"{g.Count()} {g.Key}"))}");
            others.Should().OnlyContain(u => (u.Status == UserStatus.Imported || u.Status == UserStatus.ImportedLeft || u.Status == UserStatus.ImportedElsewhere) && u.PrivateChannelId == null,
                "they stay imported, not onboarded");
        }

        private async Task<string> SendAsync(FakeInteraction interaction)
        {
            var built = interaction.Build();
            var responder = new RecordingResponder(null, built.ChannelId, built.MessageId, false);
            await _dispatcher.DispatchAsync(built, responder);
            return responder.LastReply?.Content;
        }

        private Task PostedAllAsync(string what) =>
            UntilAsync(what, () => Task.FromResult(_posting.Status().State == HistoryPostingState.Idle && !Mocker.Resolve<IHistoryPostRepository>().Waiting().Any()));

        // The bot's messages in one of the check's channels, oldest first.
        private async Task<IReadOnlyList<IMessage>> OwnAsync(ulong channelId)
        {
            var channel = (IMessageChannel)await _client.Rest.GetChannelAsync(channelId);
            return (await channel.GetMessagesAsync(100).FlattenAsync()).Where(m => m.Author.Id == _client.CurrentUser.Id).OrderBy(m => m.Id).ToList();
        }

        private static async Task UntilAsync(string what, Func<Task<bool>> isDone)
        {
            for (var i = 0; i < 300; i++)
            {
                if (await isDone())
                {
                    return;
                }

                await Task.Delay(2000);
            }

            Assert.Fail($"After 10 minutes it still isn't so that {what}");
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
