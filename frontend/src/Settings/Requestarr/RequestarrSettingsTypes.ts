export type SettingValue = string | number | boolean;

// Values by setting name; a secret's value says only whether one is saved.
export interface AppSettings {
  provisioned: boolean;
  values: Record<string, SettingValue>;
}

export type SettingInput = 'text' | 'number' | 'check' | 'password' | 'select';

export interface SettingsSection {
  legend: string;
  fields: [name: string, input: SettingInput][];
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
  botState: BotState;
  botProblem?: string;
  inviteUrl?: string;
}

export interface InstanceSettings {
  implementation: string;
  name: string;
  url: string;
  apiKey?: string;
  qualityProfile?: string;
  rootFolder?: string;
  seriesType?: string;
  isDefault: boolean;
  enabled: boolean;
  pollingOnly: boolean;
  animeEnabled: boolean;
  animePriority: string;
  dubRequired?: string;
  dubIgnored?: string;
  subRequired?: string;
  subIgnored?: string;
}

export interface Instance {
  id: number;
  settings: InstanceSettings;
  apiKeySet: boolean;
  connected: boolean;
  problem?: string;
}

export interface InstanceTestResult {
  problem?: string;
  qualityProfiles: string[];
  rootFolders: string[];
}
