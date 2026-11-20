import { filterTypes } from 'Helpers/Props';
import { accessText, isImported, memberStatusText } from 'Portal/portalText';
import { createThunk, handleThunks } from 'Store/thunks';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import translate from 'Utilities/String/translate';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';

// Every member Requestarr knows, as Prowlarr's indexers section holds its items (Prowlarr
// Store/Actions/indexerActions.js); how the Members page shows them is in memberIndexActions.js.

export const section = 'members';

// The content types a member's access columns show, in the order Requestarr lists them.
export const ACCESS_TYPES = ['movie', 'tv', 'music'];

export function accessFor(member, type) {
  const access = member.access.find((a) => a.contentType === type);

  return access ? accessText(access) : translate('PortalAccessOff');
}

function statusFilter(status) {
  return {
    key: status,
    label: () => memberStatusText(status),
    filters: [{ key: 'status', value: status, type: filterTypes.EQUAL }]
  };
}

export const filters = [
  { key: 'all', label: () => translate('All'), filters: [] },
  statusFilter('active'),
  statusFilter('suspended'),
  {
    key: 'imported',
    label: () => translate('PortalImportedMembers'),
    filters: [{ key: 'imported', value: true, type: filterTypes.EQUAL }]
  }
];

export const filterPredicates = {
  // Known only from an import, whichever kind.
  imported: (item, value) => isImported(item.status) === value,
  added: (item, value, type) => dateFilterPredicate(item.added, value, type),
  firstRequest: (item, value, type) => dateFilterPredicate(item.firstRequest, value, type),
  lastRequest: (item, value, type) => dateFilterPredicate(item.lastRequest, value, type)
};

// Sorted by the words shown, not the codes underneath.
export const sortPredicates = {
  name: (item) => item.name.toLowerCase(),
  status: (item) => memberStatusText(item.status),
  byType: (item) => item.requests,
  ...Object.fromEntries(ACCESS_TYPES.map((type) => [`access-${type}`, (item) => accessFor(item, type)]))
};

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  items: []
};

//
// Actions Types

export const FETCH_MEMBERS = 'members/fetchMembers';

//
// Action Creators

export const fetchMembers = createThunk(FETCH_MEMBERS);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_MEMBERS]: createFetchHandler(section, '/portal/members')
});

//
// Reducers

export const reducers = createHandleActions({}, defaultState, section);
