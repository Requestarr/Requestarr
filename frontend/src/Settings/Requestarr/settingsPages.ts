import { SettingsSection } from './RequestarrSettingsTypes';

// Which of Requestarr's settings each settings page shows, in the order of the provisioning file they replace.
export const DISCORD_CONNECTION: SettingsSection = {
  legend: 'SettingsDiscordConnection',
  fields: [
    ['discordBotToken', 'password'],
    ['discordApplicationId', 'text'],
    ['discordGuildId', 'text'],
    ['discordAdminRoleId', 'text'],
    ['discordAdminRoleName', 'text'],
  ],
};

export const DISCORD_SECTIONS: SettingsSection[] = [
  DISCORD_CONNECTION,
  {
    legend: 'SettingsBotProfile',
    fields: [
      ['botProfileKeepUpToDate', 'check'],
      ['botProfileTakeOver', 'check'],
    ],
  },
];

export const REQUEST_SECTIONS: SettingsSection[] = [
  {
    legend: 'SettingsRemovalVotes',
    fields: [
      ['removalVoteDays', 'number'],
      ['removalRequesterTimeoutDays', 'number'],
      ['removalReminderDay', 'number'],
    ],
  },
  {
    legend: 'SettingsAccessRequests',
    fields: [
      ['accessDenialCooldownDays', 'number'],
      ['accessSpamWindowMinutes', 'number'],
      ['accessShortCooldownClicks', 'number'],
      ['accessShortCooldownMinutes', 'number'],
      ['accessLongCooldownClicks', 'number'],
      ['accessLongCooldownHours', 'number'],
      ['accessBlockClicks', 'number'],
      ['accessBlockWithinHours', 'number'],
    ],
  },
  {
    legend: 'SettingsAuditLog',
    fields: [['auditLogKeepFor', 'select']],
  },
];

// The choices of each 'select' setting: the value the settings API takes, and the translation key of its words.
export const SETTING_CHOICES: Record<string, [value: string, label: string][]> =
  {
    auditLogKeepFor: [
      ['90 days', 'AuditLogKeep90Days'],
      ['6 months', 'AuditLogKeep6Months'],
      ['1 year', 'AuditLogKeep1Year'],
      ['2 years', 'AuditLogKeep2Years'],
      ['forever', 'AuditLogKeepForever'],
    ],
  };

export const CONNECTION_SECTIONS: SettingsSection[] = [
  {
    legend: 'SettingsDownloads',
    fields: [
      ['availabilityWebhookUrl', 'text'],
      ['availabilityPollMinutes', 'number'],
    ],
  },
  {
    legend: 'SettingsTmdb',
    fields: [['tmdbApiKey', 'password']],
  },
  {
    legend: 'SettingsBugReports',
    fields: [
      ['bugReportsFileIssues', 'check'],
      ['bugReportsRepository', 'text'],
      ['bugReportsGithubToken', 'password'],
      ['bugReportsPerDay', 'number'],
      ['bugReportsMinutesBetween', 'number'],
    ],
  },
];
