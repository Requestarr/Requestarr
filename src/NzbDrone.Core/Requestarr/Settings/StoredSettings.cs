using NzbDrone.Core.Configuration;
using Requestarr.Core.Availability;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Settings
{
    // A provisioning file keeps the bot's connection and the download checks in memory only. Without a file (beta and
    // production) the web app saves them in Requestarr's settings, and this puts them where the bot and the download
    // checks read them: on start, and after every change. The Movie Database and GitHub keys come from the server's
    // environment instead (TmdbSettingsStore, BugReportSettings), never from the web app.
    public interface IStoredSettings
    {
        void Load();
    }

    public class StoredSettings : IStoredSettings
    {
        private readonly IConfigService _config;
        private readonly IDiscordSettingsStore _discord;
        private readonly IAvailabilitySettingsStore _availability;

        public StoredSettings(IConfigService config,
                              IDiscordSettingsStore discord,
                              IAvailabilitySettingsStore availability)
        {
            _config = config;
            _discord = discord;
            _availability = availability;
        }

        public void Load()
        {
            _discord.Set(ProvisioningService.DiscordSettingsFrom(new DiscordProvisioning
            {
                BotToken = _config.DiscordBotToken,
                ApplicationId = _config.DiscordApplicationId,
                GuildId = _config.DiscordGuildId,
                AdminRoleId = _config.DiscordAdminRoleId,
                AdminRoleName = _config.DiscordAdminRoleName
            }));

            _availability.Set(_config.AvailabilityWebhookUrl, _config.AvailabilityPollMinutes);
        }
    }
}
