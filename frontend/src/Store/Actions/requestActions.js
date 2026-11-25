import { filterTypes } from 'Helpers/Props';
import { contentTypeText, requestStatusText } from 'Portal/portalText';
import { createThunk, handleThunks } from 'Store/thunks';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import { isInState } from './requestStates';

// Every request, as Sonarr's series section has them (Sonarr main Store/Actions/seriesActions.js); how the Requests
// page shows them is in requestIndexActions.js.

export const section = 'requests';

// The requester and every follower.
export function peopleOf(request) {
  return [request.requester, ...request.followers];
}

function hasPerson(request, id) {
  return peopleOf(request).some((person) => person.id === id);
}

export const filterPredicates = {
  state(item, value, type) {
    const has = isInState(item, value);

    return type === filterTypes.NOT_EQUAL ? !has : has;
  },

  // Asked for or followed by.
  member(item, value, type) {
    const has = hasPerson(item, value);

    return type === filterTypes.NOT_EQUAL ? !has : has;
  },

  requested(item, value, type) {
    return dateFilterPredicate(item.requested, value, type);
  },

  decided(item, value, type) {
    return dateFilterPredicate(item.decided, value, type);
  },

  fulfilled(item, value, type) {
    return dateFilterPredicate(item.fulfilled, value, type);
  }
};

// Sorted by the words shown, not the codes underneath.
export const sortPredicates = {
  title: (item) => item.title.toLowerCase(),
  type: (item) => contentTypeText(item.contentType),
  status: (item) => requestStatusText(item),
  requester: (item) => (item.requester.name ?? '').toLowerCase(),
  followers: (item) => item.followers.length,
  instance: (item) => item.instance ?? ''
};

export function uniqueBy(items, option) {
  const seen = new Map();

  items.forEach((item) => {
    const found = option(item);

    if (found && !seen.has(found.id)) {
      seen.set(found.id, found);
    }
  });

  return [...seen.values()];
}

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

export const FETCH_REQUESTS = 'requests/fetchRequests';

//
// Action Creators

export const fetchRequests = createThunk(FETCH_REQUESTS);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_REQUESTS]: createFetchHandler(section, '/portal/requests')
});

//
// Reducers

export const reducers = createHandleActions({}, defaultState, section);
