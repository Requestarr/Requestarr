// What /api/v1/requestarr/import/... answers (src/NzbDrone.Core/Requestarr/Imports). Discord IDs are text: they're
// too long for a browser's numbers. Empty values are left out.
export interface ImportAccess {
  readInviteUrl?: string;
  messageContent?: boolean;
  portalUrl?: string;
}

export interface ImportChannel {
  id: string;
  server?: string;
  name?: string;
  canRead: boolean;
  problem?: string;
}

interface ImportPerson {
  discordId: string;
  name: string;
  userId?: number;
}

interface ImportFollower {
  person: ImportPerson;
  asked: string;
}

export interface PlannedRequest {
  type: 'movie' | 'tv';
  title: string;
  year?: number;
  instance?: string;
  requester: ImportPerson;
  requested: string;
  seasons: number[];
  available?: string;
  hasFiles: boolean;
  outcome: 'sent' | 'alreadyAvailable';
  inLibrary: boolean;
  followers: ImportFollower[];
}

export interface PlannedFollow {
  requestId: number;
  type: 'movie' | 'tv';
  title: string;
  year?: number;
  requester?: string;
  requested: string;
  instance?: string;
  followers: ImportFollower[];
}

interface PlannedLink {
  requestId: number;
  title: string;
  instance: string;
}

export interface ImportPlan {
  requests: PlannedRequest[];
  follows: PlannedFollow[];
  links: PlannedLink[];
  notRequests: number;
  noRequester: number;
  messagesRead: number;
  alreadyImported: number;
}

interface ImportProgress {
  channelId: string;
  name?: string;
  read: number;
  done: boolean;
}

export interface ImportJob {
  id: string;
  state: 'reading' | 'ready' | 'failed' | 'imported';
  channels: ImportProgress[];
  problem?: string;
  warning?: string;
  plan?: ImportPlan;
  result?: {
    requests: number;
    notInLibrary: number;
    followers: number;
    people: number;
    linked: number;
  };
}
