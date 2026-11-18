using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Discord;
using Discord.Rest;
using FluentAssertions;
using Moq;
using NLog;
using NUnit.Framework;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common.Categories;
using Requestarr.Core.Access;
using Requestarr.Core.Admin;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Portal;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Test.Discord.Harness;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Users
{
    // Live: the test member renamed in the dev server, with the real bot. Their server display name is changed by the
    // bot, and Discord's own update about it is what Requestarr acts on; the stored name, the channel's topic, their
    // History and their portal page are read back. A username can only be changed by its owner, so that one is the
    // rename missed while the bot was offline: their stored username, channel and role are set to an old name, and the
    // start's catch-up renames the channel and role in Discord back to their username, read back from Discord. Their
    // display name is put back afterwards. The member stays, active, for the portal's pages.
    // Never runs in CI (ManualTest). Needs the stack's Requestarr stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveMemberRenamesFixture : DbTest
    {
        private const string NewDisplayName = "Renamed by the live check";
        private const string OldUsername = "renames-check-old";

        private static readonly TimeSpan UpdateWait = TimeSpan.FromSeconds(30);

        private readonly TaskCompletionSource<GuildMember> _seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        [Test]
        public async Task a_rename_reaches_the_stored_name_channel_role_history_and_portal()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN not set");
            }

            var (host, users) = Start();
            RestGuildUser member = null;
            string nickname = null;

            try
            {
                await host.StartAsync(Mocker.Resolve<IDiscordSettingsStore>().Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
                var guild = await host.Client.Rest.GetGuildAsync(Mocker.Resolve<IDiscordSettingsStore>().Current.GuildId);
                member = LiveTestMembers.Pick(await guild.GetUsersAsync().FlattenAsync()).First();
                nickname = member.Nickname;
                Log($"[member] test member: {member.Username}, display name {member.DisplayName}");

                (await users.AddAsync(member.Id, new Dictionary<ContentType, AccessState> { [ContentType.Movie] = AccessState.NeedsApproval }, Actor.Web("qa-checks"))).Success.Should().BeTrue();
                var user = users.FindByDiscordId(member.Id);

                await DisplayNameChanged(host, member, user);
                await UsernameCaughtUp(host, member, user);
            }
            finally
            {
                if (member != null)
                {
                    // Their own nickname again, or none.
                    await member.ModifyAsync(p => p.Nickname = nickname);
                    Log($"[cleanup] display name put back to {nickname ?? "their own, with no server nickname"}");
                }

                if (host.Client?.GetGuild(Mocker.Resolve<IDiscordSettingsStore>().Current.GuildId) is { } connected)
                {
                    await connected.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-dev to restore the dev stack)");
            }
        }

        // The bot changes their display name; Discord tells the bot, and that update is what's acted on.
        private async Task DisplayNameChanged(DiscordClientHost host, RestGuildUser member, RequestarrUser user)
        {
            var before = user.DisplayName;
            await member.ModifyAsync(p => p.Nickname = NewDisplayName);
            var seen = await _seen.Task.WaitAsync(UpdateWait);
            Log($"[event] Discord's update arrived: display name {seen.DisplayName}");

            (await Mocker.Resolve<MemberNameService>().RefreshAsync(seen)).Should().BeTrue();

            var stored = Mocker.Resolve<IUserManagementService>().FindByDiscordId(member.Id).DisplayName;
            var topic = ((RestTextChannel)await host.Client.Rest.GetChannelAsync(ulong.Parse(user.PrivateChannelId))).Topic;
            var detail = Mocker.Resolve<PortalQueries>().Member(user.Id);
            var renamed = detail.History[^1];
            Log($"[stored] display name: {stored}");
            Log($"[channel] topic, read back from Discord: \"{topic}\"");
            Log($"[history] {renamed.What} · {renamed.Who} · {renamed.Detail}");
            Log($"[portal] member page: {detail.Member.Name}, Discord name {detail.Member.DiscordName}, Discord ID {detail.Member.DiscordId}");

            stored.Should().Be(NewDisplayName);
            topic.Should().Be(ChannelLayout.UserChannelTopic(NewDisplayName));
            renamed.What.Should().Be("Renamed");
            renamed.Detail.Should().Be($"from {before} to {NewDisplayName}");
            detail.Member.Name.Should().Be(NewDisplayName);
            detail.Member.DiscordId.Should().Be(member.Id.ToString());
        }

        // A username changed while the bot was offline: the start's catch-up renames their channel and role.
        private async Task UsernameCaughtUp(DiscordClientHost host, RestGuildUser member, RequestarrUser user)
        {
            var gateway = Mocker.Resolve<IDiscordGuildGateway>();
            var identity = Mocker.Resolve<IUserIdentityRepository>().Find(UserIdentity.DiscordProvider, member.Id.ToString());
            identity.Username = OldUsername;
            Mocker.Resolve<IUserIdentityRepository>().Update(identity);
            var channelId = ulong.Parse(user.PrivateChannelId);
            var roleId = ulong.Parse(user.DiscordRoleId);
            await gateway.RenameChannelAsync(channelId, ChannelLayout.UserChannelName(OldUsername));
            await gateway.RenameRoleAsync(roleId, ChannelLayout.UserRoleName(OldUsername));
            Log($"[before] stored username @{OldUsername}, channel #{ChannelLayout.UserChannelName(OldUsername)}, role {ChannelLayout.UserRoleName(OldUsername)}");

            await Mocker.Resolve<MemberNameService>().CatchUpAsync();

            var channel = ((RestTextChannel)await host.Client.Rest.GetChannelAsync(channelId)).Name;
            var role = (await host.Client.Rest.GetGuildAsync(Mocker.Resolve<IDiscordSettingsStore>().Current.GuildId)).GetRole(roleId).Name;
            var renamed = Mocker.Resolve<PortalQueries>().Member(user.Id).History[^1];
            var found = Mocker.Resolve<PortalAuditLog>().Search(OldUsername).Members.Select(m => m.Name).ToList();
            Log($"[after] channel, read back from Discord: #{channel}; role: {role}");
            Log($"[history] {renamed.What} · {renamed.Who} · {renamed.Detail}");
            Log($"[search] \"{OldUsername}\" finds: {string.Join(", ", found)}");

            channel.Should().Be(ChannelLayout.UserChannelName(member.Username));
            role.Should().Be(ChannelLayout.UserRoleName(member.Username));
            renamed.Detail.Should().Be($"Discord username from @{OldUsername} to @{member.Username}");
            found.Should().Equal(NewDisplayName);
        }

        // The services as the app has them, over a real database and the real bot; as LivePortalActionsFixture builds
        // them. Discord's member updates are caught where the app hands them on.
        private (DiscordClientHost Host, IUserManagementService Users) Start()
        {
            UseRealHttp();
            var settings = new DiscordSettingsStore();
            settings.Set(LiveDev.Settings());
            Mocker.SetConstant<IDiscordSettingsStore>(settings);
            UseRealRepositories();
            Mocker.SetConstant<IMediaServerConnections>(new FakeMediaServers());
            Mocker.GetMock<IEventAggregator>().Setup(e => e.PublishEvent(It.IsAny<DiscordMemberSeenEvent>()))
                .Callback<DiscordMemberSeenEvent>(e =>
                {
                    if (e.Member.DisplayName == NewDisplayName)
                    {
                        _seen.TrySetResult(e.Member);
                    }
                });

            IGuildSetupService guildSetup = null;
            IUserManagementService users = null;
            IPanelService panels = null;
            var host = new DiscordClientHost(settings,
                new Lazy<IInteractionDispatcher>(() => null),
                new Lazy<IGuildSetupService>(() => guildSetup),
                new Lazy<IUserManagementService>(() => users),
                new Lazy<IPanelService>(() => panels),
                new Lazy<IRequestFeed>(() => Mocker.GetMock<IRequestFeed>().Object),
                new Lazy<IAccessRequestService>(() => null),
                Mocker.Resolve<IEventAggregator>(),
                LogManager.GetLogger("LiveDiscord"));
            Mocker.SetConstant<IDiscordClientHost>(host);
            Mocker.SetConstant<IDiscordGuildGateway>(new DiscordNetGateway(host, settings));
            guildSetup = Mocker.Resolve<GuildSetupService>();
            Mocker.SetConstant(guildSetup);
            Mocker.SetConstant<IAdminQueue>(new AdminQueue(Array.Empty<IAdminQueueSource>()));
            panels = Mocker.Resolve<PanelService>();
            Mocker.SetConstant(panels);
            users = Mocker.Resolve<UserManagementService>();
            Mocker.SetConstant(users);
            Mocker.SetConstant<IPortalQueries>(Mocker.Resolve<PortalQueries>());

            return (host, users);
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
            Mocker.SetConstant<IAccessRequestRepository>(Mocker.Resolve<AccessRequestRepository>());
        }

        private static void Log(string line) => TestContext.Progress.WriteLine(line);
    }
}
