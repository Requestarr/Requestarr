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
    // Live: the test member renamed, with the real bot in the dev server. Discord's own update about the member is what
    // Requestarr acts on: the bot gives them a nickname when it may (Manage Nicknames); otherwise their stored name is
    // set to an old one, a rename Requestarr missed, and a role taken and given back makes Discord send the update. The
    // stored name, the channel's topic, their History and their portal page are read back. A username can only be
    // changed by the account's owner, so that one is the rename missed while the bot was offline: their stored
    // username, channel and role are set to an old name, and the start's catch-up renames the channel and role in
    // Discord back to their username, read back from Discord. A nickname given is taken away afterwards. The member
    // stays, active, for the portal's pages.
    // Never runs in CI (ManualTest). Needs the stack's Requestarr stopped: docker/dev.sh reset-dev --stopped.
    [TestFixture]
    [ManualTest]
    public class LiveMemberRenamesFixture : DbTest
    {
        private const string NewDisplayName = "Renamed by the live check";
        private const string OldDisplayName = "Before the live check";
        private const string OldUsername = "renames-check-old";

        private static readonly TimeSpan UpdateWait = TimeSpan.FromSeconds(30);

        private readonly TaskCompletionSource<GuildMember> _seen = new(TaskCreationOptions.RunContinuationsAsynchronously);

        // The display name Discord's update must carry once the rename is staged; null until then.
        private volatile string _awaited;

        [Test]
        public async Task a_rename_reaches_the_stored_name_channel_role_history_and_portal()
        {
            if (string.IsNullOrWhiteSpace(LiveDev.Token))
            {
                Assert.Ignore("DEV_DISCORD_BOT_TOKEN not set");
            }

            var (host, users) = Start();
            RestGuildUser member = null;
            var nicknameGiven = false;

            try
            {
                await host.StartAsync(Mocker.Resolve<IDiscordSettingsStore>().Current);
                await host.WaitUntilReadyAsync(TimeSpan.FromSeconds(60));
                var guild = await host.Client.Rest.GetGuildAsync(Mocker.Resolve<IDiscordSettingsStore>().Current.GuildId);
                member = LiveTestMembers.Pick(await guild.GetUsersAsync().FlattenAsync()).First();
                Log($"[member] test member: {member.Username}, display name {member.DisplayName}");

                (await users.AddAsync(member.Id, new Dictionary<ContentType, AccessState> { [ContentType.Movie] = AccessState.NeedsApproval }, Actor.Web("qa-checks"))).Success.Should().BeTrue();
                var user = users.FindByDiscordId(member.Id);

                nicknameGiven = await DisplayNameFollowsDiscord(host, guild, member, user);
                await UsernameCaughtUp(host, member, user);
            }
            finally
            {
                if (nicknameGiven)
                {
                    await member.ModifyAsync(p => p.Nickname = null);
                    Log("[cleanup] the nickname given is taken away");
                }

                if (host.Client?.GetGuild(Mocker.Resolve<IDiscordSettingsStore>().Current.GuildId) is { } connected)
                {
                    await connected.BulkOverwriteApplicationCommandAsync(Array.Empty<ApplicationCommandProperties>());
                }

                await host.StopAsync();
                Log("[cleanup] bot disconnected (run docker/dev.sh reset-dev to restore the dev stack)");
            }
        }

        // Discord's update about the member carries their display name now, and Requestarr's stored one follows it. True
        // when the bot gave them a nickname for it.
        private async Task<bool> DisplayNameFollowsDiscord(DiscordClientHost host, RestGuild guild, RestGuildUser member, RequestarrUser user)
        {
            var bot = await guild.GetCurrentUserAsync();
            var mayRename = bot.GuildPermissions.ManageNicknames && guild.OwnerId != member.Id;
            Log($"[permission] the dev bot {(mayRename ? "may" : "may not")} change the test member's nickname");
            var before = mayRename ? user.DisplayName : OldDisplayName;
            var after = mayRename ? NewDisplayName : member.DisplayName;

            if (mayRename)
            {
                _awaited = after;
                await member.ModifyAsync(p => p.Nickname = NewDisplayName);
            }
            else
            {
                user.DisplayName = before;
                Mocker.Resolve<IRequestarrUserRepository>().Update(user);
                Log($"[before] stored display name set to \"{before}\" (a rename Requestarr missed); Discord says \"{after}\"");
                _awaited = after;
                var role = ulong.Parse(user.DiscordRoleId);
                await host.Client.Rest.RemoveRoleAsync(guild.Id, member.Id, role);
                await host.Client.Rest.AddRoleAsync(guild.Id, member.Id, role);
            }

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

            stored.Should().Be(after);
            topic.Should().Be(ChannelLayout.UserChannelTopic(after));
            renamed.What.Should().Be("Renamed");
            renamed.Detail.Should().Be($"from \"{before}\" to \"{after}\"");
            detail.Member.Name.Should().Be(after);
            detail.Member.DiscordId.Should().Be(member.Id.ToString());

            return mayRename;
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
            found.Should().Equal(Mocker.Resolve<IUserManagementService>().FindByDiscordId(member.Id).DisplayName);
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
                    if (_awaited != null && e.Member.DisplayName == _awaited)
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
                new Lazy<Requestarr.Core.Settings.IDiscordServerChoice>(() => null),
                new Lazy<IDiscordAdmins>(() => null),
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
