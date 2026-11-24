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

// A Discord server the bot is or was in. Ours: the one Requestarr runs in; any other it only reads, for an import.
// Joined: the bot is in it now; otherwise its reconnectUrl adds it back. Its ID is text: it's too long for a browser's
// numbers.
export interface DiscordServer {
  id: string;
  name?: string;
  iconUrl?: string;
  isOurs: boolean;
  reachable: boolean;
  joined: boolean;
  reconnectUrl?: string;
}

interface DiscordBot {
  name: string;
  avatarUrl?: string;
}

// InviteUrl: the full invite, offered only while Requestarr has no server. PickServer: the bot is in several and none
// is Requestarr's yet. BetaFromImage: the beta image says this is beta.
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
  bot?: DiscordBot;
  servers: DiscordServer[];
  pickServer: boolean;
  betaFromImage: boolean;
  provisioned: boolean;
}

// Someone in Requestarr's server, for picking admins.
export interface AdminCandidate {
  discordId: string;
  name: string;
  username: string;
  avatar?: string;
  isAdmin: boolean;
  isOwner: boolean;
}
