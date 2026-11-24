using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Portal;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Settings
{
    // A new install without a provisioning file starts with nothing set. Needed: some step isn't done yet, so the web
    // app opens the first-run setup: the admin's sign-in (the sign-in dialog every fresh install shows), the Discord
    // bot, and at least one Sonarr or Radarr unless the admin skipped that step (InstanceSkipped: they add them later on
    // the Connections page). Discord is done once the bot has a server: the token is all the admin types, and the bot
    // reads its application and server from Discord. InviteUrl adds the bot to Requestarr's server, offered while it
    // isn't in one (none chosen yet, or removed from it); the read-only one adds it to another server only to read
    // there, for an import from Requestrr.
    // PickServer: the bot is in several servers and none is Requestarr's yet. BetaFromImage: the beta image says this
    // is beta, shown read-only.
    public record SetupStatus(bool Needed, bool AdminAccount, bool Discord, bool Instance, bool InstanceSkipped, DiscordBotState BotState, string BotProblem,
                              string InviteUrl, string ReadOnlyInviteUrl)
    {
        public DiscordBotIdentity Bot { get; init; }
        public IReadOnlyList<DiscordServer> Servers { get; init; } = new List<DiscordServer>();
        public bool PickServer { get; init; }
        public bool BetaFromImage { get; init; }
        public bool Provisioned { get; init; }
    }

    public interface ISetupService
    {
        SetupStatus Status();

        // The first-run setup's Sonarr or Radarr step, left for later.
        SetupStatus SkipInstance();

        // Every change made to the settings and the instances, newest first.
        IReadOnlyList<PortalEvent> Changes();
    }

    public class SetupService : ISetupService
    {
        // The permissions the setup guide's invite link asks for (docs/setup-guide.md, section 2).
        public const string InvitePermissions = "2251800082205712";

        // Inviting the bot to another server only so an import can read it: View Channels and Read Message History,
        // and no commands.
        public const string ReadOnlyInvitePermissions = "66560";
        public const string ReadOnlyInviteScope = "bot";
        private const string InviteScope = "bot+applications.commands";

        private readonly IProvisioningService _provisioning;
        private readonly IConfigFileProvider _configFile;
        private readonly IConfigService _config;
        private readonly IDiscordSettingsStore _discord;
        private readonly IDiscordClientHost _discordHost;
        private readonly IDiscordServers _discordServers;
        private readonly IInstanceSettingsService _instances;
        private readonly IAuditRepository _audit;
        private readonly IReleaseEnvironment _environment;

        public SetupService(IProvisioningService provisioning,
                            IConfigFileProvider configFile,
                            IConfigService config,
                            IDiscordSettingsStore discord,
                            IDiscordClientHost discordHost,
                            IDiscordServers discordServers,
                            IInstanceSettingsService instances,
                            IAuditRepository audit,
                            IReleaseEnvironment environment)
        {
            _provisioning = provisioning;
            _configFile = configFile;
            _config = config;
            _discord = discord;
            _discordHost = discordHost;
            _discordServers = discordServers;
            _instances = instances;
            _audit = audit;
            _environment = environment;
        }

        public SetupStatus Status()
        {
            var adminAccount = _configFile.AuthenticationMethod != AuthenticationType.None;
            var discord = _discord.Current.IsConfigured;
            var instance = _instances.All().Any(i => i.Settings.Enabled);
            var skipped = _config.SetupInstanceSkipped;
            var applicationId = _discord.Current.ApplicationId;
            var servers = _discordServers.All();
            var hasServer = _discord.Current.GuildId != 0;
            var inServer = servers.Any(s => s.IsOurs && s.Reachable);
            var invite = inServer ? null : InviteUrl(applicationId, InvitePermissions, InviteScope);
            var readOnlyInvite = InviteUrl(applicationId, ReadOnlyInvitePermissions, ReadOnlyInviteScope);

            var needed = !_provisioning.UsesFile && !(adminAccount && discord && (instance || skipped));

            return new SetupStatus(needed, adminAccount, discord, instance, skipped, _discordHost.State, _discordHost.LastError, invite, readOnlyInvite)
            {
                Bot = _discordHost.Bot,
                Servers = servers.Select(s => s.Joined ? s : s with { ReconnectUrl = ReconnectUrl(applicationId, s) }).ToList(),
                PickServer = !hasServer && servers.Count(s => s.Joined) > 1,
                BetaFromImage = _environment.BetaFromImage,
                Provisioned = _provisioning.UsesFile
            };
        }

        public SetupStatus SkipInstance()
        {
            _config.SetupInstanceSkipped = true;

            return Status();
        }

        // Null until the bot's application ID is known.
        public static string InviteUrl(ulong applicationId, string permissions, string scope) =>
            applicationId == 0 ? null : $"https://discord.com/oauth2/authorize?client_id={applicationId}&permissions={permissions}&integration_type=0&scope={scope}";

        // Discord's invite with the server already picked.
        private static string ReconnectUrl(ulong applicationId, DiscordServer server)
        {
            var invite = server.IsOurs
                ? InviteUrl(applicationId, InvitePermissions, InviteScope)
                : InviteUrl(applicationId, ReadOnlyInvitePermissions, ReadOnlyInviteScope);

            return invite == null ? null : $"{invite}&guild_id={server.Id}&disable_guild_select=true";
        }

        public IReadOnlyList<PortalEvent> Changes() =>
            _audit.ForTargetTypes("settings", "instance").Select(e => PortalText.Event(e, new Dictionary<int, string>(), new Dictionary<string, string>())).ToList();
    }
}
