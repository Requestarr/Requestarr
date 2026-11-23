using System.Globalization;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Audit;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Settings
{
    // What the bot learns from Discord with its token alone, saved as if the admin had typed it: its application, and
    // the server it runs in (the one it joined, or the one the admin picked). A provisioning file (dev and QA) sets both,
    // so nothing is saved there.
    public interface IDiscordServerChoice
    {
        void SaveApplication(ulong applicationId);

        // False on a server with a provisioning file, whose file says which server it is.
        bool SaveServer(ulong guildId, Actor actor);
    }

    public class DiscordServerChoice : IDiscordServerChoice
    {
        public const string ServerKey = "discordGuildId";

        private readonly IConfigService _config;
        private readonly IStoredSettings _stored;
        private readonly IProvisioningService _provisioning;
        private readonly IAuditService _audit;

        public DiscordServerChoice(IConfigService config, IStoredSettings stored, IProvisioningService provisioning, IAuditService audit)
        {
            _config = config;
            _stored = stored;
            _provisioning = provisioning;
            _audit = audit;
        }

        public void SaveApplication(ulong applicationId)
        {
            if (_provisioning.UsesFile || _config.DiscordApplicationId == Text(applicationId))
            {
                return;
            }

            _config.DiscordApplicationId = Text(applicationId);
            _stored.Load();
        }

        public bool SaveServer(ulong guildId, Actor actor)
        {
            if (_provisioning.UsesFile)
            {
                return false;
            }

            var before = _config.DiscordGuildId;

            if (before != Text(guildId))
            {
                _config.DiscordGuildId = Text(guildId);
                _stored.Load();
                _audit.Record(actor, AppSettingsService.ChangeAction, "settings", ServerKey, $"Server: from {(string.IsNullOrEmpty(before) ? "nothing" : before)} to {guildId}");
            }

            return true;
        }

        private static string Text(ulong id) => id.ToString(CultureInfo.InvariantCulture);
    }
}
