using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NLog;
using NzbDrone.Common.EnsureThat;
using NzbDrone.Common.Http.Proxy;
using NzbDrone.Core.Configuration.Events;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.Security;

namespace NzbDrone.Core.Configuration
{
    public enum ConfigKey
    {
        DownloadedMoviesFolder
    }

    public class ConfigService : IConfigService
    {
        private readonly IConfigRepository _repository;
        private readonly IEventAggregator _eventAggregator;
        private readonly Logger _logger;
        private static Dictionary<string, string> _cache;

        public ConfigService(IConfigRepository repository, IEventAggregator eventAggregator, Logger logger)
        {
            _repository = repository;
            _eventAggregator = eventAggregator;
            _logger = logger;
            _cache = new Dictionary<string, string>();
        }

        private Dictionary<string, object> AllWithDefaults()
        {
            var dict = new Dictionary<string, object>(StringComparer.InvariantCultureIgnoreCase);

            var type = GetType();
            var properties = type.GetProperties();

            foreach (var propertyInfo in properties)
            {
                var value = propertyInfo.GetValue(this, null);
                dict.Add(propertyInfo.Name, value);
            }

            return dict;
        }

        public void SaveConfigDictionary(Dictionary<string, object> configValues)
        {
            var allWithDefaults = AllWithDefaults();

            foreach (var configValue in configValues)
            {
                allWithDefaults.TryGetValue(configValue.Key, out var currentValue);
                if (currentValue == null || configValue.Value == null)
                {
                    continue;
                }

                var equal = configValue.Value.ToString().Equals(currentValue.ToString());

                if (!equal)
                {
                    SetValue(configValue.Key, configValue.Value.ToString());
                }
            }

            _eventAggregator.PublishEvent(new ConfigSavedEvent());
        }

        public bool IsDefined(string key)
        {
            return _repository.Get(key.ToLower()) != null;
        }

        public int HistoryCleanupDays
        {
            get { return GetValueInt("HistoryCleanupDays", 30); }
            set { SetValue("HistoryCleanupDays", value); }
        }

        public int RemovalVoteDays
        {
            get { return GetValueInt("RemovalVoteDays", Requestarr.Core.Removals.RemovalSettings.DefaultVoteDays); }
            set { SetValue("RemovalVoteDays", value); }
        }

        public int RemovalRequesterTimeoutDays
        {
            get { return GetValueInt("RemovalRequesterTimeoutDays", Requestarr.Core.Removals.RemovalSettings.DefaultRequesterTimeoutDays); }
            set { SetValue("RemovalRequesterTimeoutDays", value); }
        }

        // Off until an admin turns it on: a server sharing its Sonarr and Radarr must never lose files to a mistake.
        public bool RemovalDeleteFiles
        {
            get { return GetValueBoolean("RemovalDeleteFiles", false); }
            set { SetValue("RemovalDeleteFiles", value); }
        }

        public string LastRunVersion
        {
            get { return GetValue("LastRunVersion"); }
            set { SetValue("LastRunVersion", value ?? string.Empty); }
        }

        public int RemovalReminderDay
        {
            get { return GetValueInt("RemovalReminderDay", Requestarr.Core.Removals.RemovalSettings.DefaultReminderDay); }
            set { SetValue("RemovalReminderDay", value); }
        }

        public int AccessDenialCooldownDays
        {
            get { return GetValueInt("AccessDenialCooldownDays", Requestarr.Core.Access.AccessSettings.DefaultDenialCooldownDays); }
            set { SetValue("AccessDenialCooldownDays", value); }
        }

        public int AccessSpamWindowMinutes
        {
            get { return GetValueInt("AccessSpamWindowMinutes", Requestarr.Core.Access.AccessSettings.DefaultSpamWindowMinutes); }
            set { SetValue("AccessSpamWindowMinutes", value); }
        }

        public int AccessShortCooldownClicks
        {
            get { return GetValueInt("AccessShortCooldownClicks", Requestarr.Core.Access.AccessSettings.DefaultShortCooldownClicks); }
            set { SetValue("AccessShortCooldownClicks", value); }
        }

        public int AccessShortCooldownMinutes
        {
            get { return GetValueInt("AccessShortCooldownMinutes", Requestarr.Core.Access.AccessSettings.DefaultShortCooldownMinutes); }
            set { SetValue("AccessShortCooldownMinutes", value); }
        }

        public int AccessLongCooldownClicks
        {
            get { return GetValueInt("AccessLongCooldownClicks", Requestarr.Core.Access.AccessSettings.DefaultLongCooldownClicks); }
            set { SetValue("AccessLongCooldownClicks", value); }
        }

        public int AccessLongCooldownHours
        {
            get { return GetValueInt("AccessLongCooldownHours", Requestarr.Core.Access.AccessSettings.DefaultLongCooldownHours); }
            set { SetValue("AccessLongCooldownHours", value); }
        }

        public int AccessBlockClicks
        {
            get { return GetValueInt("AccessBlockClicks", Requestarr.Core.Access.AccessSettings.DefaultBlockClicks); }
            set { SetValue("AccessBlockClicks", value); }
        }

        public int AccessBlockWithinHours
        {
            get { return GetValueInt("AccessBlockWithinHours", Requestarr.Core.Access.AccessSettings.DefaultBlockWithinHours); }
            set { SetValue("AccessBlockWithinHours", value); }
        }

        public bool BotProfileKeepUpToDate
        {
            get { return GetValueBoolean("BotProfileKeepUpToDate", true); }
            set { SetValue("BotProfileKeepUpToDate", value); }
        }

        public bool BotProfileTakeOver
        {
            get { return GetValueBoolean("BotProfileTakeOver", false); }
            set { SetValue("BotProfileTakeOver", value); }
        }

        public string BotProfileAvatarSet
        {
            get { return GetValue("BotProfileAvatarSet"); }
            set { SetValue("BotProfileAvatarSet", value ?? string.Empty); }
        }

        public string BotProfileBannerSet
        {
            get { return GetValue("BotProfileBannerSet"); }
            set { SetValue("BotProfileBannerSet", value ?? string.Empty); }
        }

        public bool SetupInstanceSkipped
        {
            get { return GetValueBoolean("SetupInstanceSkipped", false); }
            set { SetValue("SetupInstanceSkipped", value); }
        }

        public bool BugReportsFileIssues
        {
            get { return GetValueBoolean("BugReportsFileIssues", false); }
            set { SetValue("BugReportsFileIssues", value); }
        }

        public string BugReportsRepository
        {
            get { return GetValue("BugReportsRepository"); }
            set { SetValue("BugReportsRepository", value ?? string.Empty); }
        }

        public int BugReportsPerDay
        {
            get { return GetValueInt("BugReportsPerDay", Requestarr.Core.Qa.BugReportSettings.DefaultReportsPerDay); }
            set { SetValue("BugReportsPerDay", value); }
        }

        public int BugReportsMinutesBetween
        {
            get { return GetValueInt("BugReportsMinutesBetween", Requestarr.Core.Qa.BugReportSettings.DefaultMinutesBetween); }
            set { SetValue("BugReportsMinutesBetween", value); }
        }

        public string AuditLogKeepFor
        {
            get { return GetValue("AuditLogKeepFor", Requestarr.Core.Audit.AuditRetention.Default); }
            set { SetValue("AuditLogKeepFor", value ?? string.Empty); }
        }

        public string ServerRole
        {
            get { return GetValue("ServerRole", Requestarr.Core.Announcements.ReleaseEnvironment.Production); }
            set { SetValue("ServerRole", value ?? string.Empty); }
        }

        public string AuditLogTrimmedBefore
        {
            get { return GetValue("AuditLogTrimmedBefore"); }
            set { SetValue("AuditLogTrimmedBefore", value ?? string.Empty); }
        }

        public string DiscordBotToken
        {
            get { return GetValue("DiscordBotToken"); }
            set { SetValue("DiscordBotToken", value ?? string.Empty); }
        }

        public string DiscordApplicationId
        {
            get { return GetValue("DiscordApplicationId"); }
            set { SetValue("DiscordApplicationId", value ?? string.Empty); }
        }

        public string DiscordGuildId
        {
            get { return GetValue("DiscordGuildId"); }
            set { SetValue("DiscordGuildId", value ?? string.Empty); }
        }

        public string DiscordAdminRoleId
        {
            get { return GetValue("DiscordAdminRoleId"); }
            set { SetValue("DiscordAdminRoleId", value ?? string.Empty); }
        }

        public string DiscordAdminRoleName
        {
            get { return GetValue("DiscordAdminRoleName"); }
            set { SetValue("DiscordAdminRoleName", value ?? string.Empty); }
        }

        public string AvailabilityWebhookUrl
        {
            get { return GetValue("AvailabilityWebhookUrl"); }
            set { SetValue("AvailabilityWebhookUrl", value ?? string.Empty); }
        }

        public int AvailabilityPollMinutes
        {
            get { return GetValueInt("AvailabilityPollMinutes", Requestarr.Core.Availability.AvailabilitySettingsStore.DefaultPollMinutes); }
            set { SetValue("AvailabilityPollMinutes", value); }
        }

        public bool LogIndexerResponse
        {
            get { return GetValueBoolean("LogIndexerResponse", false); }

            set { SetValue("LogIndexerResponse", value); }
        }

        public int FirstDayOfWeek
        {
            get { return GetValueInt("FirstDayOfWeek", (int)CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek); }

            set { SetValue("FirstDayOfWeek", value); }
        }

        public string CalendarWeekColumnHeader
        {
            get { return GetValue("CalendarWeekColumnHeader", "ddd M/D"); }

            set { SetValue("CalendarWeekColumnHeader", value); }
        }

        public string ShortDateFormat
        {
            get { return GetValue("ShortDateFormat", "MMM D YYYY"); }

            set { SetValue("ShortDateFormat", value); }
        }

        public string LongDateFormat
        {
            get { return GetValue("LongDateFormat", "dddd, MMMM D YYYY"); }

            set { SetValue("LongDateFormat", value); }
        }

        public string TimeFormat
        {
            get { return GetValue("TimeFormat", "h(:mm)a"); }

            set { SetValue("TimeFormat", value); }
        }

        public bool ShowRelativeDates
        {
            get { return GetValueBoolean("ShowRelativeDates", true); }

            set { SetValue("ShowRelativeDates", value); }
        }

        public bool EnableColorImpairedMode
        {
            get { return GetValueBoolean("EnableColorImpairedMode", false); }

            set { SetValue("EnableColorImpairedMode", value); }
        }

        public string UILanguage
        {
            get { return GetValue("UILanguage", "en"); }

            set { SetValue("UILanguage", value); }
        }

        public string PlexClientIdentifier => GetValue("PlexClientIdentifier", Guid.NewGuid().ToString(), true);

        public string RijndaelPassphrase => GetValue("RijndaelPassphrase", Guid.NewGuid().ToString(), true);

        public string HmacPassphrase => GetValue("HmacPassphrase", Guid.NewGuid().ToString(), true);

        public string RijndaelSalt => GetValue("RijndaelSalt", Guid.NewGuid().ToString(), true);

        public string HmacSalt => GetValue("HmacSalt", Guid.NewGuid().ToString(), true);

        public string DownloadProtectionKey => GetValue("DownloadProtectionKey", Guid.NewGuid().ToString().Replace("-", ""), true);

        public bool ProxyEnabled => GetValueBoolean("ProxyEnabled", false);

        public ProxyType ProxyType => GetValueEnum<ProxyType>("ProxyType", ProxyType.Http);

        public string ProxyHostname => GetValue("ProxyHostname", string.Empty);

        public int ProxyPort => GetValueInt("ProxyPort", 8080);

        public string ProxyUsername => GetValue("ProxyUsername", string.Empty);

        public string ProxyPassword => GetValue("ProxyPassword", string.Empty);

        public string ProxyBypassFilter => GetValue("ProxyBypassFilter", string.Empty);

        public bool ProxyBypassLocalAddresses => GetValueBoolean("ProxyBypassLocalAddresses", true);

        public string BackupFolder => GetValue("BackupFolder", "Backups");

        public int BackupInterval => GetValueInt("BackupInterval", 7);

        public int BackupRetention => GetValueInt("BackupRetention", 28);

        public CertificateValidationType CertificateValidation =>
            GetValueEnum("CertificateValidation", CertificateValidationType.Enabled);

        public string ApplicationUrl => GetValue("ApplicationUrl", string.Empty);

        public bool TrustCgnatIpAddresses
        {
            get { return GetValueBoolean("TrustCgnatIpAddresses", false); }
            set { SetValue("TrustCgnatIpAddresses", value); }
        }

        private string GetValue(string key)
        {
            return GetValue(key, string.Empty);
        }

        private bool GetValueBoolean(string key, bool defaultValue = false)
        {
            return Convert.ToBoolean(GetValue(key, defaultValue));
        }

        private int GetValueInt(string key, int defaultValue = 0)
        {
            return Convert.ToInt32(GetValue(key, defaultValue));
        }

        private T GetValueEnum<T>(string key, T defaultValue)
        {
            return (T)Enum.Parse(typeof(T), GetValue(key, defaultValue), true);
        }

        public string GetValue(string key, object defaultValue, bool persist = false)
        {
            key = key.ToLowerInvariant();
            Ensure.That(key, () => key).IsNotNullOrWhiteSpace();

            EnsureCache();

            if (_cache.TryGetValue(key, out var dbValue) && dbValue != null && !string.IsNullOrEmpty(dbValue))
            {
                return dbValue;
            }

            _logger.Trace("Using default config value for '{0}' defaultValue:'{1}'", key, defaultValue);

            if (persist)
            {
                SetValue(key, defaultValue.ToString());
            }

            return defaultValue.ToString();
        }

        private void SetValue(string key, bool value)
        {
            SetValue(key, value.ToString());
        }

        private void SetValue(string key, int value)
        {
            SetValue(key, value.ToString());
        }

        private void SetValue(string key, Enum value)
        {
            SetValue(key, value.ToString().ToLower());
        }

        private void SetValue(string key, string value)
        {
            key = key.ToLowerInvariant();

            _logger.Trace("Writing Setting to database. Key:'{0}' Value:'{1}'", key, value);
            _repository.Upsert(key, value);

            ClearCache();
        }

        private void EnsureCache()
        {
            lock (_cache)
            {
                if (!_cache.Any())
                {
                    var all = _repository.All();
                    _cache = all.ToDictionary(c => c.Key.ToLower(), c => c.Value);
                }
            }
        }

        private static void ClearCache()
        {
            lock (_cache)
            {
                _cache = new Dictionary<string, string>();
            }
        }
    }
}
