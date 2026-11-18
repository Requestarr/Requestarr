import moment from 'moment';
import translate from 'Utilities/String/translate';
import {
  AccessState,
  ContentType,
  MemberStatus,
  PortalAccess,
  PortalRequest,
  RequestStatus,
} from './PortalTypes';

// The portal's words for what the API sends as codes, the same words Discord's cards use.
const CONTENT_TYPES: Record<ContentType, string> = {
  movie: 'PortalMovie',
  tv: 'PortalTv',
  music: 'PortalMusic',
  comic: 'PortalComic',
  book: 'PortalBook',
};

const ACCESS: Record<AccessState, string> = {
  off: 'PortalAccessOff',
  needsApproval: 'PortalAccessNeedsApproval',
  autoApprove: 'PortalAccessAutoApprove',
};

const MEMBER_STATUS: Record<MemberStatus, string> = {
  active: 'PortalMemberActive',
  suspended: 'PortalMemberSuspended',
  removed: 'PortalMemberRemoved',
};

const QUEUE_KINDS: Record<string, string> = {
  request: 'PortalQueueRequest',
  trouble: 'PortalQueueTrouble',
  mediaServerLink: 'PortalQueueMediaServerLink',
  instanceDown: 'PortalQueueInstanceDown',
  removal: 'PortalQueueRemoval',
  accessRequest: 'PortalQueueAccessRequest',
};

function capitalised(code: string) {
  return code.charAt(0).toUpperCase() + code.slice(1);
}

export function contentTypeText(type: ContentType) {
  return translate(CONTENT_TYPES[type]);
}

export function accessStateText(state: AccessState) {
  return translate(ACCESS[state]);
}

export function memberStatusText(status: MemberStatus) {
  return translate(MEMBER_STATUS[status]);
}

export function queueKindText(kind: string) {
  return QUEUE_KINDS[kind] ? translate(QUEUE_KINDS[kind]) : kind;
}

export function statusText(status: RequestStatus) {
  return translate(`PortalStatus${capitalised(status)}`);
}

// "Approved", or while Sonarr or Radarr has it, "Approved: downloading".
export function requestStatusText(request: PortalRequest) {
  const status = statusText(request.status);
  const hasProgress =
    request.status === 'approved' && request.progress !== 'none';

  return hasProgress
    ? `${status}: ${translate(
        `PortalProgress${capitalised(request.progress)}`
      )}`
    : status;
}

export function removalStatusText(status: string) {
  return translate(`PortalRemoval${capitalised(status)}`);
}

// "Needs approval, 3 every 7 days" or "Off".
export function accessText(access: PortalAccess) {
  const level = accessStateText(access.state);

  if (access.state === 'off' || access.quotaLimit == null) {
    return level;
  }

  const limit = translate('PortalLimit', {
    limit: access.quotaLimit,
    days: access.quotaDays ?? 0,
  });

  return `${level}, ${limit}`;
}

export function titleText(request: { title: string; year: number | null }) {
  return request.year ? `${request.title} (${request.year})` : request.title;
}

export function seasonsText(seasons: number[]) {
  return seasons.length
    ? translate('PortalSeasonsList', { seasons: seasons.join(', ') })
    : '';
}

// Requestarr answers in Discord's words, with Discord's markup: bold, mentions and times. In the web app those are
// plain text and a readable time.
export function plainText(discord: string) {
  return discord
    .replace(/<t:(\d+)(:[a-zA-Z])?>/g, (_, seconds: string) =>
      moment.unix(Number(seconds)).format('MMMM D, YYYY, h:mma')
    )
    .replace(/\*\*(.+?)\*\*/g, '$1')
    .replace(/<#\d+>/g, translate('PortalTheirChannel'))
    .replace(/<@!?\d+>\s*/g, '');
}
