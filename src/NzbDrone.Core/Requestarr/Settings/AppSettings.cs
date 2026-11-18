using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Metadata;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Qa;

namespace Requestarr.Core.Settings
{
    public enum SettingKind
    {
        Text,
        Number,
        Switch,

        // Saved but never shown back: the settings pages only say whether one is set.
        Secret
    }

    // One of Requestarr's settings as the web app's settings pages have it: its name in the API, its name in the change
    // record, and where its value lives. Check returns why a value can't be used, or null.
    public record SettingDefinition(string Key, string Label, SettingKind Kind, Func<string> Read, Action<string> Write, int Min = 0, Func<string, string> Check = null);

    // Values by the settings' API names: text as text, numbers as numbers, switches as true or false, and secrets as
    // whether one is set.
    public record AppSettingsView(bool Provisioned, IReadOnlyDictionary<string, object> Values);

    public record SettingsSaveResult(bool Saved, string Problem)
    {
        public static readonly SettingsSaveResult Done = new(true, null);

        public static SettingsSaveResult Refused(string problem) => new(false, problem);
    }

    public interface IAppSettingsService
    {
        AppSettingsView Get();

        // Only the settings named are changed; each change is recorded with who made it and what it was before.
        Task<SettingsSaveResult> SaveAsync(IReadOnlyDictionary<string, string> changes, Actor actor);
    }

    // Every setting the provisioning file has, for beta and production, where there is no file: the settings pages
    // read and change them here. With a file (dev and QA) they're read-only, since the file would undo a change on the
    // next start.
    public class AppSettingsService : IAppSettingsService
    {
        public const string ChangeAction = "settings.change";

        // Every number here is days, hours, minutes or clicks: this is more than any needs (27 years in days) and small
        // enough that no time Requestarr works out from it overflows.
        public const int MaxNumber = 10000;

        public const string ProvisionedProblem = "These settings come from the provisioning file on this server; change them there.";

        private const string WebhookAddress = "availabilityWebhookUrl";

        private static readonly Regex DiscordId = new(@"^\d{15,21}$");
        private static readonly Regex Repository = new(@"^[\w.-]+/[\w.-]+$");

        private static readonly HashSet<string> DiscordConnection = new() { "discordBotToken", "discordApplicationId", "discordGuildId", "discordAdminRoleId", "discordAdminRoleName" };

        private readonly IConfigService _config;
        private readonly IProvisioningService _provisioning;
        private readonly IStoredSettings _stored;
        private readonly IDiscordSettingsStore _discord;
        private readonly IDiscordClientHost _discordHost;
        private readonly ITmdbSettingsStore _tmdb;
        private readonly IAvailabilitySettingsStore _availability;
        private readonly IBugReportSettings _bugReports;
        private readonly IAuditSettings _auditLog;
        private readonly IBackendService _backends;
        private readonly IReleaseEnvironment _environment;
        private readonly IAuditService _audit;

        public AppSettingsService(IConfigService config,
                                  IProvisioningService provisioning,
                                  IStoredSettings stored,
                                  IDiscordSettingsStore discord,
                                  IDiscordClientHost discordHost,
                                  ITmdbSettingsStore tmdb,
                                  IAvailabilitySettingsStore availability,
                                  IBugReportSettings bugReports,
                                  IAuditSettings auditLog,
                                  IBackendService backends,
                                  IReleaseEnvironment environment,
                                  IAuditService audit)
        {
            _config = config;
            _provisioning = provisioning;
            _stored = stored;
            _discord = discord;
            _discordHost = discordHost;
            _tmdb = tmdb;
            _availability = availability;
            _bugReports = bugReports;
            _auditLog = auditLog;
            _backends = backends;
            _environment = environment;
            _audit = audit;
        }

        public AppSettingsView Get() => new(_provisioning.UsesFile, Definitions().ToDictionary(d => d.Key, Shown));

        public async Task<SettingsSaveResult> SaveAsync(IReadOnlyDictionary<string, string> changes, Actor actor)
        {
            if (_provisioning.UsesFile)
            {
                return SettingsSaveResult.Refused(ProvisionedProblem);
            }

            var definitions = Definitions().ToDictionary(d => d.Key);

            if (changes.Select(c => Problem(definitions, c.Key, c.Value)).FirstOrDefault(p => p != null) is { } problem)
            {
                return SettingsSaveResult.Refused(problem);
            }

            var changed = changes.Where(c => Apply(definitions[c.Key], c.Value, actor)).Select(c => c.Key).ToList();

            if (changed.Any())
            {
                _stored.Load();
            }

            if (changed.Any(DiscordConnection.Contains))
            {
                await _discordHost.RestartAsync();
            }

            // Sonarr's and Radarr's webhooks follow the address: checking the instances adds, moves or removes them.
            if (changed.Contains(WebhookAddress))
            {
                await _backends.CheckAllAsync();
            }

            return SettingsSaveResult.Done;
        }

        // Writes the value and records the change; false when it was already that.
        private bool Apply(SettingDefinition definition, string value, Actor actor)
        {
            var before = definition.Read() ?? string.Empty;
            var after = Normalized(definition.Kind, value);

            if (before == after)
            {
                return false;
            }

            definition.Write(after);
            _audit.Record(actor, ChangeAction, "settings", definition.Key, Described(definition, before, after));

            return true;
        }

        private static string Problem(IReadOnlyDictionary<string, SettingDefinition> definitions, string key, string value)
        {
            if (!definitions.TryGetValue(key, out var definition))
            {
                return $"There's no setting called {key}.";
            }

            return definition.Kind switch
            {
                SettingKind.Number when !int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) || number < definition.Min || number > MaxNumber =>
                    $"{definition.Label} must be a whole number from {definition.Min} to {MaxNumber}.",
                SettingKind.Switch when !bool.TryParse(value, out _) => $"{definition.Label} is on or off.",
                _ => definition.Check?.Invoke(Normalized(definition.Kind, value))
            };
        }

        private static string Normalized(SettingKind kind, string value) => kind switch
        {
            SettingKind.Number => int.Parse(value, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture),
            SettingKind.Switch => bool.Parse(value) ? "true" : "false",
            _ => value?.Trim() ?? string.Empty
        };

        private static object Shown(SettingDefinition definition)
        {
            var value = definition.Read() ?? string.Empty;

            return definition.Kind switch
            {
                SettingKind.Number => int.Parse(value, CultureInfo.InvariantCulture),
                SettingKind.Switch => value == "true",
                SettingKind.Secret => value.Length > 0,
                _ => value
            };
        }

        // "Vote length (days): from 7 to 5"; a secret only says that it changed.
        private static string Described(SettingDefinition definition, string before, string after)
        {
            if (definition.Kind != SettingKind.Secret)
            {
                return $"{definition.Label}: from {Words(definition.Kind, before)} to {Words(definition.Kind, after)}";
            }

            if (after.Length == 0)
            {
                return $"{definition.Label}: removed";
            }

            return $"{definition.Label}: {(before.Length == 0 ? "set" : "replaced")}";
        }

        private static string Words(SettingKind kind, string value)
        {
            if (kind == SettingKind.Switch)
            {
                return value == "true" ? "on" : "off";
            }

            return value.Length == 0 ? "nothing" : value;
        }

        private IEnumerable<SettingDefinition> Definitions() => DiscordDefinitions().Concat(RequestDefinitions()).Concat(ConnectionDefinitions());

        private IEnumerable<SettingDefinition> DiscordDefinitions() => new[]
        {
            new SettingDefinition("discordBotToken", "Bot token", SettingKind.Secret, () => _discord.Current.BotToken, v => _config.DiscordBotToken = v),
            new SettingDefinition("discordApplicationId", "Application ID", SettingKind.Text, () => Id(_discord.Current.ApplicationId), v => _config.DiscordApplicationId = v, Check: v => IdProblem("Application ID", v)),
            new SettingDefinition("discordGuildId", "Server ID", SettingKind.Text, () => Id(_discord.Current.GuildId), v => _config.DiscordGuildId = v, Check: v => IdProblem("Server ID", v)),
            new SettingDefinition("discordAdminRoleId", "Admin role ID", SettingKind.Text, () => _discord.Current.AdminRoleId?.ToString(CultureInfo.InvariantCulture), v => _config.DiscordAdminRoleId = v, Check: v => IdProblem("Admin role ID", v)),
            new SettingDefinition("discordAdminRoleName", "Admin role name", SettingKind.Text, () => _discord.Current.AdminRoleName, v => _config.DiscordAdminRoleName = v),
            Switch("botProfileKeepUpToDate", "Keep the bot's picture and banner up to date", () => _config.BotProfileKeepUpToDate, v => _config.BotProfileKeepUpToDate = v),
            Switch("botProfileTakeOver", "Replace a picture or banner chosen by hand", () => _config.BotProfileTakeOver, v => _config.BotProfileTakeOver = v)
        };

        private IEnumerable<SettingDefinition> RequestDefinitions() => new[]
        {
            Number("removalVoteDays", "Removal vote length (days)", () => _config.RemovalVoteDays, v => _config.RemovalVoteDays = v, 1),
            Number("removalRequesterTimeoutDays", "Days the requester has to answer a removal", () => _config.RemovalRequesterTimeoutDays, v => _config.RemovalRequesterTimeoutDays = v, 1),
            Number("removalReminderDay", "Day the requester is reminded", () => _config.RemovalReminderDay, v => _config.RemovalReminderDay = v, 0),
            Number("accessDenialCooldownDays", "Days before asking again after a denial", () => _config.AccessDenialCooldownDays, v => _config.AccessDenialCooldownDays = v, 0),
            Number("accessSpamWindowMinutes", "Spam guard: minutes clicks count for", () => _config.AccessSpamWindowMinutes, v => _config.AccessSpamWindowMinutes = v, 1),
            Number("accessShortCooldownClicks", "Spam guard: clicks before a short wait", () => _config.AccessShortCooldownClicks, v => _config.AccessShortCooldownClicks = v, 1),
            Number("accessShortCooldownMinutes", "Spam guard: short wait (minutes)", () => _config.AccessShortCooldownMinutes, v => _config.AccessShortCooldownMinutes = v, 1),
            Number("accessLongCooldownClicks", "Spam guard: clicks before a long wait", () => _config.AccessLongCooldownClicks, v => _config.AccessLongCooldownClicks = v, 1),
            Number("accessLongCooldownHours", "Spam guard: long wait (hours)", () => _config.AccessLongCooldownHours, v => _config.AccessLongCooldownHours = v, 1),
            Number("accessBlockClicks", "Spam guard: more clicks that block", () => _config.AccessBlockClicks, v => _config.AccessBlockClicks = v, 1),
            Number("accessBlockWithinHours", "Spam guard: hours those clicks count for", () => _config.AccessBlockWithinHours, v => _config.AccessBlockWithinHours = v, 1),
            new SettingDefinition("auditLogKeepFor", "Keep the audit log for", SettingKind.Text, () => _auditLog.KeepFor, v => _config.AuditLogKeepFor = v, Check: AuditRetention.Problem)
        };

        private IEnumerable<SettingDefinition> ConnectionDefinitions() => new[]
        {
            new SettingDefinition(WebhookAddress, "Address Sonarr and Radarr reach Requestarr at", SettingKind.Text, () => _availability.WebhookUrl, v => _config.AvailabilityWebhookUrl = v, Check: UrlProblem),
            Number("availabilityPollMinutes", "Minutes between download checks", () => _availability.PollMinutes, v => _config.AvailabilityPollMinutes = v, AvailabilitySettingsStore.MinPollMinutes),
            new SettingDefinition("tmdbApiKey", "The Movie Database key", SettingKind.Secret, () => _tmdb.ApiKey, v => _config.TmdbApiKey = v),
            new SettingDefinition("bugReportsFileIssues", "File bug reports on GitHub", SettingKind.Switch, () => _bugReports.FileIssues ? "true" : "false", v => _config.BugReportsFileIssues = v == "true", Check: FileIssuesProblem),
            new SettingDefinition("bugReportsRepository", "GitHub repository for bug reports", SettingKind.Text, () => _config.BugReportsRepository, v => _config.BugReportsRepository = v, Check: RepositoryProblem),
            new SettingDefinition("bugReportsGithubToken", "GitHub key for bug reports", SettingKind.Secret, () => _bugReports.Token, v => _config.BugReportsGithubToken = v),
            Number("bugReportsPerDay", "Bug reports a person can send a day", () => _config.BugReportsPerDay, v => _config.BugReportsPerDay = v, 1),
            Number("bugReportsMinutesBetween", "Minutes between two bug reports", () => _config.BugReportsMinutesBetween, v => _config.BugReportsMinutesBetween = v, 0)
        };

        private static SettingDefinition Number(string key, string label, Func<int> read, Action<int> write, int min) =>
            new(key, label, SettingKind.Number, () => read().ToString(CultureInfo.InvariantCulture), v => write(int.Parse(v, CultureInfo.InvariantCulture)), min);

        private static SettingDefinition Switch(string key, string label, Func<bool> read, Action<bool> write) =>
            new(key, label, SettingKind.Switch, () => read() ? "true" : "false", v => write(v == "true"));

        private static string Id(ulong id) => id == 0 ? string.Empty : id.ToString(CultureInfo.InvariantCulture);

        private static string IdProblem(string label, string value) =>
            value.Length == 0 || DiscordId.IsMatch(value) ? null : $"{label} is the long number Discord shows with Developer Mode on (Copy ID).";

        internal static bool IsWebAddress(string value) =>
            Uri.TryCreate(value, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

        private static string UrlProblem(string value) =>
            value.Length == 0 || IsWebAddress(value) ? null : "The address starts with http:// or https://, for example http://requestarr:9797.";

        // The dev, QA and beta servers file every report, so the gate before staging sees them (BugReportSettings).
        private string FileIssuesProblem(string value) =>
            value == "false" && _environment.IconSet != ReleaseEnvironment.Production ? "Test servers (dev, QA and beta) always file bug reports on GitHub." : null;

        private static string RepositoryProblem(string value) =>
            value.Length == 0 || Repository.IsMatch(value) ? null : "The repository is written owner/name, for example Requestarr/Requestarr.";
    }
}
