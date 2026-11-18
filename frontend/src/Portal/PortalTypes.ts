// What /api/v1/portal/... answers (src/NzbDrone.Core/Requestarr/Portal/PortalModels.cs).
export type ContentType = 'movie' | 'tv' | 'music' | 'comic' | 'book';

export type RequestStatus =
  | 'pending'
  | 'approved'
  | 'denied'
  | 'cancelled'
  | 'failed'
  | 'available'
  | 'removed';

type DownloadProgress =
  | 'none'
  | 'downloading'
  | 'stalled'
  | 'failed'
  | 'imported';

export type MemberStatus = 'active' | 'suspended' | 'removed';

export type AccessState = 'off' | 'needsApproval' | 'autoApprove';

export interface PortalPerson {
  id: number;
  name: string | null;
}

export interface PortalRequest {
  id: number;
  title: string;
  year: number | null;
  contentType: ContentType;
  status: RequestStatus;
  progress: DownloadProgress;
  requester: PortalPerson;
  followers: PortalPerson[];
  instanceId: number | null;
  instance: string | null;
  seasons: number[];
  requested: string;
  decided: string | null;
  fulfilled: string | null;
}

export interface PortalEvent {
  at: string;
  what: string;
  who: string;
  detail: string | null;
}

export interface PortalRemoval {
  id: number;
  status: string;
  seasons: number[];
  created: string;
  closed: string | null;
  startedBy: string | null;
  closedBy: string | null;
  keeps: string[];
  isOpen: boolean;
}

export interface PortalRequestDetail {
  request: PortalRequest;
  decisionReason: string | null;
  progressDetail: string | null;
  availableSeasons: number[];
  posterUrl: string | null;
  latestRemoval: PortalRemoval | null;
  timeline: PortalEvent[];
  actions: string[];
}

export interface PortalAccess {
  contentType: ContentType;
  state: AccessState;
  quotaLimit: number | null;
  quotaDays: number | null;
  instance: string | null;
}

export interface PortalMember {
  id: number;
  name: string;
  discordName: string | null;
  status: MemberStatus;
  added: string;
  access: PortalAccess[];
  requests: number;
  active: number;
  pending: number;
  following: number;
}

export interface PortalMemberDetail {
  member: PortalMember;
  requests: PortalRequest[];
  following: PortalRequest[];
  removals: PortalRemoval[];
  mediaServer: string | null;
  history: PortalEvent[];
}

export interface PortalQueueItem {
  key: string;
  kind: string;
  since: string;
  title: string | null;
  who: string | null;
  requestId: number | null;
  memberId: number | null;
}

export interface PortalInstance {
  id: number;
  name: string;
  contentType: ContentType;
  enabled: boolean;
}

export interface PortalAccessRequest {
  id: number;
  discordId: string;
  name: string;
  username: string;
  contentTypes: ContentType[];
  note: string | null;
  requested: string;
}

export interface PortalPaused {
  discordId: string;
  name: string;
  since: string;
}

export interface PortalAccessRequests {
  waiting: PortalAccessRequest[];
  paused: PortalPaused[];
}

export interface PortalOutageNow {
  instanceId: number;
  instance: string;
  since: string;
  announceOffered: boolean;
  announced: boolean;
  announcedBy: string | null;
}

export interface PortalOutage {
  instance: string;
  started: string;
  ended: string | null;
  announced: string | null;
  announcedBy: string | null;
}

export interface PortalOutages {
  now: PortalOutageNow[];
  history: PortalOutage[];
}

export interface PortalAuditEntry extends PortalEvent {
  about: string | null;
  requestId: number | null;
  memberId: number | null;
}

export interface PortalSearchResults {
  requests: PortalRequest[];
  members: PortalMember[];
}

export interface PortalServerMember {
  discordId: string;
  name: string;
  username: string;
}

export interface PortalLibraryTitle {
  instanceId: number;
  itemId: number;
  instance: string;
  title: string;
  requester: string | null;
  seasons: number[];
}

export interface PortalVersion {
  version: string;
  date: string | null;
  users: string[];
  admins: string[];
}

export interface PortalTitleRemoval {
  title: string;
  removal: PortalRemoval;
}
