export type SettingValue = string | number | boolean;

// Values by setting name; a secret's value says only whether one is saved.
export interface AppSettings {
  provisioned: boolean;
  values: Record<string, SettingValue>;
}

export type SettingInput = 'text' | 'number' | 'check' | 'password' | 'select';

// Advanced: the fields shown only with Show Advanced on, where a page has that button.
export interface SettingsSection {
  legend: string;
  fields: [name: string, input: SettingInput][];
  advanced?: string[];
}

export type BotState =
  | 'notConfigured'
  | 'connecting'
  | 'connected'
  | 'disconnected'
  | 'failed';

export interface SetupStatus {
  needed: boolean;
  adminAccount: boolean;
  discord: boolean;
  instance: boolean;
  instanceSkipped: boolean;
  botState: BotState;
  botProblem?: string;
  inviteUrl?: string;
  readOnlyInviteUrl?: string;
}
