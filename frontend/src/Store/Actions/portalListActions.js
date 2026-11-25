import { createAction } from 'redux-actions';
import { batchActions } from 'redux-batched-actions';
import { sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import findSelectedFilters from 'Utilities/Filter/findSelectedFilters';
import serverSideCollectionHandlers from 'Utilities/serverSideCollectionHandlers';
import getSectionState from 'Utilities/State/getSectionState';
import translate from 'Utilities/String/translate';
import { set, updateServerSideCollection } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createServerSideCollectionHandlers from './Creators/createServerSideCollectionHandlers';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';
import { fetchMembers } from './memberActions';
import { fetchRequests } from './requestActions';
import { requestFilters } from './requestStates';

// The portal's long lists read from the server a page at a time, as Servarr's System, then Events
// (Store/Actions/systemActions.js system.logs) and Prowlarr's History (historyActions.js): each a section with its page,
// page size, sort and columns, and Servarr's server-side collection handlers. A list of one member or request (their
// requests, a request's timeline) keeps that ID in its query, sent with every page; reading another page cancels the
// read before it.

export const section = 'portalLists';

// How many a page shows until Table Options changes it, remembered in this browser.
const PAGE_SIZE = 25;

function column(name, label, isSortable = false, isVisible = true) {
  return { name, label: () => translate(label), isSortable, isVisible };
}

// Where Servarr's table puts its options (the cog in the heading row), as Sonarr's History has its details column.
const optionsColumn = { name: 'details', label: '', columnLabel: () => translate('Details'), isVisible: true, isModifiable: false };

// A list's own actions column, which also holds the options cog.
const actionsColumn = { name: 'actions', label: '', columnLabel: () => translate('PortalActions'), isVisible: true, isModifiable: false };

function requestColumns(withRequester) {
  return [
    { ...column('title', 'PortalTitle', true), isModifiable: false },
    column('type', 'PortalType', true),
    column('status', 'PortalStatus', true),
    ...(withRequester ? [column('requester', 'PortalRequester', true)] : []),
    column('followers', 'PortalFollowers', true),
    column('instance', 'PortalInstance', true),
    column('requested', 'PortalRequested', true),
    optionsColumn
  ];
}

// An event's own columns, before the options column (the audit log adds its About column between).
const eventDataColumns = [
  column('at', 'PortalWhen'),
  column('what', 'PortalWhat'),
  column('who', 'PortalWho'),
  column('detail', 'PortalDetail')
];

const eventColumns = [...eventDataColumns, optionsColumn];

// A removal's columns, also a request's latest removal on its own page.
export const removalColumns = [
  column('created', 'PortalStarted'),
  column('status', 'PortalStatus'),
  column('seasons', 'PortalSeasons'),
  column('startedBy', 'PortalStartedBy'),
  column('keeps', 'PortalKeeps'),
  column('closed', 'PortalClosed'),
  column('closedBy', 'PortalClosedBy'),
  optionsColumn
];

const newestRequested = { sortKey: 'requested', sortDirection: sortDirections.DESCENDING };

// Every list starts unfiltered, with All its only filter unless it has its own.
const unfiltered = { selectedFilterKey: 'all' };
const onlyAll = [requestFilters[0]];
const requestList = { ...newestRequested, filters: requestFilters };

// Each list: where it's read from, its columns and its first sort.
const LISTS = {
  memberRequests: { url: '/portal/members/requests', columns: requestColumns(false), ...requestList },
  memberFollowing: { url: '/portal/members/following', columns: requestColumns(true), ...requestList },
  memberRemovals: { url: '/portal/members/removals', columns: removalColumns },
  memberHistory: { url: '/portal/members/history', columns: eventColumns },
  requestTimeline: { url: '/portal/requests/timeline', columns: eventColumns },
  requestFollowers: {
    url: '/portal/requests/followers',
    columns: [column('member', 'PortalMember', true), column('followed', 'PortalFollowed', true), optionsColumn],
    sortKey: 'followed',
    sortDirection: sortDirections.ASCENDING
  },
  audit: { url: '/portal/audit', columns: [...eventDataColumns, column('about', 'PortalAbout'), optionsColumn] },
  outageHistory: {
    url: '/portal/outages/history',
    columns: [
      column('instance', 'PortalInstance'),
      column('started', 'PortalStarted'),
      column('ended', 'PortalEnded'),
      column('announced', 'PortalAnnounced'),
      optionsColumn
    ]
  },
  accessWaiting: {
    url: '/portal/access-requests/waiting',
    columns: [
      column('who', 'PortalWho'),
      column('askedFor', 'PortalAskedFor'),
      column('note', 'PortalNote'),
      column('requested', 'PortalRequested'),
      actionsColumn
    ]
  },
  accessPaused: {
    url: '/portal/access-requests/paused',
    columns: [
      column('who', 'PortalWho'),
      column('since', 'PortalPausedSince'),
      actionsColumn
    ]
  },
  queue: {
    url: '/portal/queue/items',
    columns: [
      column('since', 'PortalWaitingSince'),
      column('kind', 'PortalWaitingFor'),
      column('title', 'PortalTitle'),
      column('who', 'PortalWho'),
      actionsColumn
    ]
  }
};

export const LIST_NAMES = Object.keys(LISTS);

function sectionOf(name) {
  return `${section}.${name}`;
}

function typeOf(name, action) {
  return `${section}/${name}/${action}`;
}

//
// State

export const defaultState = Object.fromEntries(LIST_NAMES.map((name) => {
  const { url, ...list } = LISTS[name];

  return [name, {
    isFetching: false,
    isPopulated: false,
    error: null,
    page: 1,
    pageSize: PAGE_SIZE,
    totalPages: 0,
    totalRecords: 0,
    sortKey: null,
    sortDirection: sortDirections.ASCENDING,
    items: [],

    // The ID and filters every page is read with (the member's ID, the audit log's search), and the Filter menu's pick.
    query: {},
    filters: onlyAll,
    ...unfiltered,
    ...list
  }];
}));

export const persistState = LIST_NAMES.flatMap((name) => [`${sectionOf(name)}.pageSize`, `${sectionOf(name)}.columns`]);

//
// Actions Types and Action Creators

const PAGE_ACTIONS = {
  [serverSideCollectionHandlers.FETCH]: 'fetch',
  [serverSideCollectionHandlers.FIRST_PAGE]: 'gotoFirstPage',
  [serverSideCollectionHandlers.PREVIOUS_PAGE]: 'gotoPreviousPage',
  [serverSideCollectionHandlers.NEXT_PAGE]: 'gotoNextPage',
  [serverSideCollectionHandlers.LAST_PAGE]: 'gotoLastPage',
  [serverSideCollectionHandlers.EXACT_PAGE]: 'gotoPage',
  [serverSideCollectionHandlers.SORT]: 'setSort',
  [serverSideCollectionHandlers.FILTER]: 'setFilter'
};

// Each list's thunks (fetch, the pager's pages, sort, filter) and table option action, by the list's name.
export const listActions = Object.fromEntries(LIST_NAMES.map((name) => [name, {
  ...Object.fromEntries(Object.values(PAGE_ACTIONS).map((action) => [action, createThunk(typeOf(name, action))])),
  setTableOption: createAction(typeOf(name, 'setTableOption'))
}]));

// Starts a list over for its query (a member's ID, the audit log's search), at its first sort and unfiltered, and reads
// its first page.
export function showList(name, query) {
  const { sortKey = null, sortDirection = sortDirections.ASCENDING } = LISTS[name];

  return (dispatch) => {
    dispatch(set({ section: sectionOf(name), query, page: 1, sortKey, sortDirection, ...unfiltered, items: [], isPopulated: false }));
    dispatch(listActions[name].fetch());
  };
}

// The lists each live message from the server (Requestarr.Http PortalBroadcaster) concerns, with the whole list it
// reads again.
const LIVE_GROUPS = {
  requests: { fetchAll: fetchRequests, section: 'requests', lists: ['memberRequests', 'memberFollowing', 'requestTimeline', 'requestFollowers', 'audit'] },
  members: { fetchAll: fetchMembers, section: 'members', lists: ['memberHistory', 'memberRemovals'] },
  queue: { lists: ['queue', 'accessWaiting', 'accessPaused', 'outageHistory'] }
};

// Reads again what's shown of a live message's group, as Servarr's SignalRConnector reads its sections again on a
// "sync": the whole list when a page has it, and each of its lists a page shows.
export function refreshLive(group) {
  return (dispatch, getState) => {
    const { fetchAll, section: allSection, lists } = LIVE_GROUPS[group];
    const state = getState();

    if (fetchAll && state[allSection].isPopulated) {
      dispatch(fetchAll());
    }

    lists
      .filter((name) => state[section][name].isPopulated)
      .forEach((name) => dispatch(listActions[name].fetch()));
  };
}

// Each list's request still on its way, cancelled when another is asked for.
const abortCurrentRequests = {};

// Servarr's createFetchServerSideCollectionHandler, with the list's query sent too and the request before it cancelled
// (as Servarr's createSaveProviderHandler cancels an earlier save), so an answer for an earlier query or page never lands
// over the one asked for last.
function createFetchListHandler(name) {
  const listSection = sectionOf(name);

  return function(getState, payload, dispatch) {
    abortCurrentRequests[listSection]?.();
    dispatch(set({ section: listSection, isFetching: true }));

    const { page, pageSize, sortKey, sortDirection, query, selectedFilterKey, filters } = getSectionState(getState(), listSection, true);
    const sort = sortKey ? { sortKey, sortDirection } : {};
    const filter = Object.fromEntries(findSelectedFilters(selectedFilterKey, filters).map(({ key, value }) => [key, value]));
    const { request, abortRequest } = createAjaxRequest({
      url: LISTS[name].url,
      data: { page: payload.page || page || 1, pageSize, ...sort, ...query, ...filter },
      traditional: true
    });

    abortCurrentRequests[listSection] = abortRequest;

    request.done((response) => {
      abortCurrentRequests[listSection] = null;
      dispatch(batchActions([
        updateServerSideCollection({ section: listSection, data: response }),
        set({ section: listSection, isFetching: false, isPopulated: true, error: null })
      ]));
    });

    request.fail((xhr) => {
      if (!xhr.aborted) {
        abortCurrentRequests[listSection] = null;
        dispatch(set({ section: listSection, isFetching: false, isPopulated: false, error: xhr }));
      }
    });
  };
}

//
// Action Handlers

export const actionHandlers = handleThunks(Object.assign({}, ...LIST_NAMES.map((name) => ({
  ...createServerSideCollectionHandlers(
    sectionOf(name),
    LISTS[name].url,
    listActions[name].fetch,
    Object.fromEntries(Object.entries(PAGE_ACTIONS).map(([handler, action]) => [handler, typeOf(name, action)]))
  ),
  [typeOf(name, 'fetch')]: createFetchListHandler(name)
}))));

//
// Reducers

export const reducers = createHandleActions(Object.fromEntries(LIST_NAMES.map((name) => [
  typeOf(name, 'setTableOption'),
  createSetTableOptionReducer(sectionOf(name))
])), defaultState, section);
