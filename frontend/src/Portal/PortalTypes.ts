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
}

export interface PortalRequestDetail {
  request: PortalRequest;
  decisionReason: string | null;
  progressDetail: string | null;
  availableSeasons: number[];
  posterUrl: string | null;
  latestRemoval: PortalRemoval | null;
  timeline: PortalEvent[];
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
