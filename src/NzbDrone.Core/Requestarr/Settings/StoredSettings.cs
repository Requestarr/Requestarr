using NzbDrone.Core.Configuration;
using Requestarr.Core.Availability;
using Requestarr.Core.Discord;
using Requestarr.Core.Metadata;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Qa;

namespace Requestarr.Core.Settings
{
    // A provisioning file keeps the bot's connection, The Movie Database key, the download checks and the GitHub key in
    // memory only. Without a file (beta and production) the web app saves them in Requestarr's settings, and this puts
    // them where the bot, cast and download checks read them: on start, and after every change.
    public interface IStoredSettings
    {
        void Load();
    }

    public class StoredSettings : IStoredSettings
    {
        private readonly IConfigService _config;
        private readonly IDiscordSettingsStore _discord;
        private readonly ITmdbSettingsStore _tmdb;
        private readonly IAvailabilitySettingsStore _availability;
        private readonly IBugReportSettings _bugReports;

        public StoredSettings(IConfigService config,
                              IDiscordSettingsStore discord,
                              ITmdbSettingsStore tmdb,
                              IAvailabilitySettingsStore availability,
                              IBugReportSettings bugReports)
        {
            _config = config;
            _discord = discord;
            _tmdb = tmdb;
            _availability = availability;
            _bugReports = bugReports;
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

            _tmdb.Set(_config.TmdbApiKey);
            _availability.Set(_config.AvailabilityWebhookUrl, _config.AvailabilityPollMinutes);
            _bugReports.Apply(new BugReportsProvisioning { GithubToken = _config.BugReportsGithubToken });
        }
    }
}
