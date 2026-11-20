import { SettingsSection } from './RequestarrSettingsTypes';

// Which of Requestarr's settings each settings page shows, in the order of the provisioning file they replace.
export const THIS_SERVER: SettingsSection = {
  legend: 'SettingsThisServer',
  fields: [['serverRole', 'select']],
};

export const DISCORD_CONNECTION: SettingsSection = {
  legend: 'SettingsDiscordConnection',
  fields: [
    ['discordBotToken', 'password'],
    ['discordApplicationId', 'text'],
    ['discordGuildId', 'text'],
    ['discordAdminRoleId', 'text'],
    ['discordAdminRoleName', 'text'],
  ],
  advanced: ['discordAdminRoleId', 'discordAdminRoleName'],
};

export const DISCORD_SECTIONS: SettingsSection[] = [
  THIS_SERVER,
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
      ['removalDeleteFiles', 'check'],
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
    serverRole: [
      ['production', 'ServerRoleProduction'],
      ['beta', 'ServerRoleBeta'],
    ],
    auditLogKeepFor: [
      ['90 days', 'AuditLogKeep90Days'],
      ['6 months', 'AuditLogKeep6Months'],
      ['1 year', 'AuditLogKeep1Year'],
      ['2 years', 'AuditLogKeep2Years'],
      ['forever', 'AuditLogKeepForever'],
    ],
  };

// The Connections page's Options, under its cards: how Sonarr and Radarr reach Requestarr, and how often it looks.
export const CONNECTION_OPTIONS: SettingsSection = {
  legend: 'Options',
  fields: [
    ['availabilityWebhookUrl', 'text'],
    ['availabilityPollMinutes', 'number'],
  ],
};

export const TMDB_SECTIONS: SettingsSection[] = [
  {
    legend: 'SettingsTmdb',
    fields: [['tmdbApiKey', 'password']],
  },
];

export const BUG_REPORT_SECTIONS: SettingsSection[] = [
  {
    legend: 'SettingsBugReports',
    fields: [
      ['bugReportsFileIssues', 'check'],
      ['bugReportsRepository', 'text'],
      ['bugReportsGithubToken', 'password'],
      ['bugReportsPerDay', 'number'],
      ['bugReportsMinutesBetween', 'number'],
    ],
    advanced: [
      'bugReportsRepository',
      'bugReportsPerDay',
      'bugReportsMinutesBetween',
    ],
  },
];
