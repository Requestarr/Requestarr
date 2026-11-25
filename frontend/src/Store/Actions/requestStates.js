import { filterTypes } from 'Helpers/Props';
import { contentTypeText, statusText } from 'Portal/portalText';
import translate from 'Utilities/String/translate';

// The states the request lists filter by, in the order a request moves through them: each one's name and whether a
// request is in it. The server filters a member's requests by the same rules
// (src/NzbDrone.Core/Requestarr/Portal/RequestStates.cs).

function isProgressing(request, ...progress) {
  return request.status === 'approved' && progress.includes(request.progress);
}

const STATES = {
  pending: { label: () => statusText('pending'), has: (request) => request.status === 'pending' },
  denied: { label: () => statusText('denied'), has: (request) => request.status === 'denied' },
  approved: { label: () => statusText('approved'), has: (request) => request.status === 'approved' },
  notAvailable: { label: () => translate('PortalFilterNotAvailable'), has: (request) => ['approved', 'failed'].includes(request.status) },
  downloading: { label: () => translate('PortalFilterDownloading'), has: (request) => isProgressing(request, 'downloading') },
  stuck: { label: () => translate('PortalFilterStuck'), has: (request) => isProgressing(request, 'stalled', 'failed') },
  available: { label: () => statusText('available'), has: (request) => request.status === 'available' },
  removed: { label: () => statusText('removed'), has: (request) => request.status === 'removed' },
  notInLibrary: { label: () => statusText('notInLibrary'), has: (request) => request.status === 'notInLibrary' }
};

const TYPES = ['movie', 'tv'];

export function stateOptions() {
  return Object.entries(STATES).map(([id, { label }]) => ({ id, name: label() }));
}

export function isInState(request, state) {
  return STATES[state].has(request);
}

function filterOn(key, value) {
  return [{ key, value, type: filterTypes.EQUAL }];
}

// The Filter menu of every list of requests.
export const requestFilters = [
  { key: 'all', label: () => translate('All'), filters: [] },
  ...Object.entries(STATES).map(([key, { label }]) => ({ key, label, filters: filterOn('state', key) })),
  ...TYPES.map((key) => ({ key, label: () => contentTypeText(key), filters: filterOn('contentType', key) }))
];
