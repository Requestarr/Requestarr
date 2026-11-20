// What /api/v1/portal/... answers (src/NzbDrone.Core/Requestarr/Portal/PortalModels.cs).
export type ContentType = 'movie' | 'tv' | 'music' | 'comic' | 'book';

export type RequestStatus =
  | 'pending'
  | 'approved'
  | 'denied'
  | 'cancelled'
  | 'failed'
  | 'available'
  | 'removed'
  | 'notInLibrary';

type DownloadProgress =
  | 'none'
  | 'downloading'
  | 'stalled'
  | 'failed'
  | 'imported';

export type MemberStatus =
  | 'active'
  | 'suspended'
  | 'removed'
  | 'imported'
  | 'importedLeft'
  | 'importedElsewhere';

export type AccessState = 'off' | 'needsApproval' | 'autoApprove';

export interface PortalPerson {
  id: number;
  name: string | null;
  avatar: string | null;
  handle: string | null;
}

// What a request's title is (src/NzbDrone.Core/Requestarr/Portal/PortalModels.cs); values not known are null.
export interface PortalMedia {
  poster?: string | null;
  overview?: string | null;
  inCinemas?: string | null;
  digitalRelease?: string | null;
  physicalRelease?: string | null;
  firstAired?: string | null;
  network?: string | null;
  page?: string | null;
  openUrl?: string | null;
  openIn?: string | null;
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
  media?: PortalMedia;

  // What an admin can do to it now, as its page offers; the removal those actions act on, while one is open.
  actions: string[];
  removalId: number | null;
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

  // A removal of a request Sonarr and Radarr don't have: Force drops it from Requestarr's lists, deleting nothing.
  recordOnly: boolean;
}

// Someone following a request, with when they followed it.
export interface PortalFollower {
  person: PortalPerson;
  followed: string;
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
  historyTrimmedAfter: string | null;
  followers: PortalFollower[];
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
  discordId: string | null;
  status: MemberStatus;
  added: string;
  access: PortalAccess[];
  requests: number;
  active: number;
  pending: number;
  following: number;
  avatar: string | null;
  firstRequest: string | null;
  lastRequest: string | null;
  byType: { contentType: ContentType; count: number }[];
}

export interface PortalMemberDetail {
  member: PortalMember;
  requests: PortalRequest[];
  following: PortalRequest[];
  removals: PortalRemoval[];
  mediaServer: string | null;
  history: PortalEvent[];
  historyTrimmedAfter: string | null;
  importedFrom: string | null;
}

export interface PortalQueueItem {
  key: string;
  kind: string;
  since: string;
  title: string | null;
  who: string | null;
  requestId: number | null;
  memberId: number | null;
  whoAvatar: string | null;
  whoHandle: string | null;

  // A removal of a request Sonarr and Radarr don't have (see PortalRemoval).
  recordOnly: boolean;
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

// Someone in the Discord server who isn't onboarded yet, for Add member.
export interface PortalServerMember {
  discordId: string;
  name: string;
  username: string;
  avatar: string | null;
  joined: string | null;
  roles: string[];
  importedRequests: number;
}

// What everyone picked gets, per content type; limit, days and instance null keep the server's.
export interface OnboardAccess {
  contentType: ContentType;
  state: AccessState;
  limit: number | null;
  days: number | null;
  instanceId: number | null;
}

export type OnboardState = 'waiting' | 'adding' | 'added' | 'failed';

export interface OnboardResult {
  discordId: string;
  name: string;
  state: OnboardState;
  message: string | null;
}

// An onboarding run: each person's part, and whether it's gone through everyone.
export interface OnboardJob {
  id: string;
  people: OnboardResult[];
  isDone: boolean;
}

export interface PortalLibraryTitle {
  instanceId: number;
  itemId: number;
  instance: string;
  // As Sonarr or Radarr has it (what the search matches); label adds the year.
  title: string;
  label: string;
  seasons: number[];
  poster: string | null;
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
