import { SettingsSection } from './RequestarrSettingsTypes';

// Which of Requestarr's settings each settings page shows, in the order of the provisioning file they replace.

// The bot's token is all an admin types: Requestarr reads its application and server from Discord.
export const BOT_TOKEN: SettingsSection = {
  legend: 'SettingsDiscordConnection',
  fields: [['discordBotToken', 'password']],
};

// Requestarr makes and uses its own admin role; a role the admin already has is for Show Advanced.
const DISCORD_CONNECTION: SettingsSection = {
  legend: 'SettingsDiscordConnection',
  fields: [
    ['discordBotToken', 'password'],
    ['discordAdminRoleId', 'text'],
    ['discordAdminRoleName', 'text'],
  ],
  advanced: ['discordAdminRoleId', 'discordAdminRoleName'],
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
    auditLogKeepFor: [
      ['90 days', 'AuditLogKeep90Days'],
      ['6 months', 'AuditLogKeep6Months'],
      ['1 year', 'AuditLogKeep1Year'],
      ['2 years', 'AuditLogKeep2Years'],
      ['forever', 'AuditLogKeepForever'],
    ],
  };
