using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Authentication;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Portal;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Settings
{
    // A new install without a provisioning file starts with nothing set. Needed: some step isn't done yet, so the web
    // app opens the first-run setup: the admin's sign-in (the sign-in dialog every fresh install shows), the Discord
    // bot, and at least one Sonarr or Radarr. InviteUrl adds the bot to a server, once its application ID is known.
    public record SetupStatus(bool Needed, bool AdminAccount, bool Discord, bool Instance, DiscordBotState BotState, string BotProblem, string InviteUrl);

    public interface ISetupService
    {
        SetupStatus Status();

        // Every change made to the settings and the instances, newest first.
        IReadOnlyList<PortalEvent> Changes();
    }

    public class SetupService : ISetupService
    {
        // The permissions the setup guide's invite link asks for (docs/setup-guide.md, section 2).
        public const string InvitePermissions = "2251800082205712";

        private readonly IProvisioningService _provisioning;
        private readonly IConfigFileProvider _configFile;
        private readonly IDiscordSettingsStore _discord;
        private readonly IDiscordClientHost _discordHost;
        private readonly IInstanceSettingsService _instances;
        private readonly IAuditRepository _audit;

        public SetupService(IProvisioningService provisioning,
                            IConfigFileProvider configFile,
                            IDiscordSettingsStore discord,
                            IDiscordClientHost discordHost,
                            IInstanceSettingsService instances,
                            IAuditRepository audit)
        {
            _provisioning = provisioning;
            _configFile = configFile;
            _discord = discord;
            _discordHost = discordHost;
            _instances = instances;
            _audit = audit;
        }

        public SetupStatus Status()
        {
            var adminAccount = _configFile.AuthenticationMethod != AuthenticationType.None;
            var discord = _discord.Current.IsConfigured;
            var instance = _instances.All().Any(i => i.Settings.Enabled);
            var applicationId = _discord.Current.ApplicationId;
            var invite = applicationId == 0
                ? null
                : $"https://discord.com/oauth2/authorize?client_id={applicationId}&permissions={InvitePermissions}&integration_type=0&scope=bot+applications.commands";

            return new SetupStatus(!_provisioning.UsesFile && !(adminAccount && discord && instance), adminAccount, discord, instance, _discordHost.State, _discordHost.LastError, invite);
        }

        public IReadOnlyList<PortalEvent> Changes() =>
            _audit.ForTargetTypes("settings", "instance").Select(e => PortalText.Event(e, new Dictionary<int, string>(), new Dictionary<string, string>())).ToList();
    }
}
