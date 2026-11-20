import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import serverSideCollectionHandlers from 'Utilities/serverSideCollectionHandlers';
import getSectionState from 'Utilities/State/getSectionState';
import translate from 'Utilities/String/translate';
import { set } from './baseActions';
import createHandleActions from './Creators/createHandleActions';
import createServerSideCollectionHandlers from './Creators/createServerSideCollectionHandlers';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

// The portal's long lists read from the server a page at a time, as Servarr's System, then Events
// (Store/Actions/systemActions.js system.logs) and Prowlarr's History (historyActions.js): each a section with its page,
// page size, sort and columns, and Servarr's server-side collection handlers. A list of one member or request (their
// requests, a request's timeline) keeps that ID in its query, sent with every page.

export const section = 'portalLists';

// How many a page shows until Table Options changes it, remembered in this browser.
const PAGE_SIZE = 25;

function column(name, label, isSortable = false, isVisible = true) {
  return { name, label: () => translate(label), isSortable, isVisible };
}

function requestColumns(withRequester) {
  return [
    { ...column('title', 'PortalTitle', true), isModifiable: false },
    column('type', 'PortalType', true),
    column('status', 'PortalStatus', true),
    ...(withRequester ? [column('requester', 'PortalRequester', true)] : []),
    column('followers', 'PortalFollowers', true),
    column('instance', 'PortalInstance', true),
    column('requested', 'PortalRequested', true)
  ];
}

const eventColumns = [
  column('at', 'PortalWhen'),
  column('what', 'PortalWhat'),
  column('who', 'PortalWho'),
  column('detail', 'PortalDetail')
];

// A removal's columns, also a request's latest removal on its own page.
export const removalColumns = [
  column('created', 'PortalStarted'),
  column('status', 'PortalStatus'),
  column('seasons', 'PortalSeasons'),
  column('startedBy', 'PortalStartedBy'),
  column('keeps', 'PortalKeeps'),
  column('closed', 'PortalClosed'),
  column('closedBy', 'PortalClosedBy')
];

const newestRequested = { sortKey: 'requested', sortDirection: sortDirections.DESCENDING };

// Each list: where it's read from, its columns and its first sort.
const LISTS = {
  memberRequests: { url: '/portal/members/requests', columns: requestColumns(false), ...newestRequested },
  memberFollowing: { url: '/portal/members/following', columns: requestColumns(true), ...newestRequested },
  memberRemovals: { url: '/portal/members/removals', columns: removalColumns },
  memberHistory: { url: '/portal/members/history', columns: eventColumns },
  requestTimeline: { url: '/portal/requests/timeline', columns: eventColumns },
  requestFollowers: {
    url: '/portal/requests/followers',
    columns: [column('member', 'PortalMember', true), column('followed', 'PortalFollowed', true)],
    sortKey: 'followed',
    sortDirection: sortDirections.ASCENDING
  },
  audit: { url: '/portal/audit', columns: [...eventColumns, column('about', 'PortalAbout')] },
  outageHistory: {
    url: '/portal/outages/history',
    columns: [
      column('instance', 'PortalInstance'),
      column('started', 'PortalStarted'),
      column('ended', 'PortalEnded'),
      column('announced', 'PortalAnnounced')
    ]
  },
  accessWaiting: {
    url: '/portal/access-requests/waiting',
    columns: [
      column('who', 'PortalWho'),
      column('askedFor', 'PortalAskedFor'),
      column('note', 'PortalNote'),
      column('requested', 'PortalRequested'),
      column('actions', 'PortalActions')
    ]
  },
  accessPaused: {
    url: '/portal/access-requests/paused',
    columns: [
      column('who', 'PortalWho'),
      column('since', 'PortalPausedSince'),
      column('actions', 'PortalActions')
    ]
  },
  queue: {
    url: '/portal/queue/items',
    columns: [
      column('since', 'PortalWaitingSince'),
      column('kind', 'PortalWaitingFor'),
      column('title', 'PortalTitle'),
      column('who', 'PortalWho'),
      column('actions', 'PortalActions')
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

    // The ID and filters every page is read with (the member's ID, the audit log's search).
    query: {},
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
  [serverSideCollectionHandlers.SORT]: 'setSort'
};

// Each list's thunks (fetch, the pager's pages, sort) and table option action, by the list's name.
export const listActions = Object.fromEntries(LIST_NAMES.map((name) => [name, {
  ...Object.fromEntries(Object.values(PAGE_ACTIONS).map((action) => [action, createThunk(typeOf(name, action))])),
  setTableOption: createAction(typeOf(name, 'setTableOption'))
}]));

// Starts a list over for its query (a member's ID, the audit log's search) and reads its first page.
export function showList(name, query) {
  return (dispatch) => {
    dispatch(set({ section: sectionOf(name), query, page: 1, items: [], isPopulated: false }));
    dispatch(listActions[name].fetch());
  };
}

// Sends the list's query with every page it reads.
function withQuery(name) {
  return (getState, payload, data) => {
    Object.assign(data, getSectionState(getState(), sectionOf(name), true).query);
  };
}

//
// Action Handlers

export const actionHandlers = handleThunks(Object.assign({}, ...LIST_NAMES.map((name) => createServerSideCollectionHandlers(
  sectionOf(name),
  LISTS[name].url,
  listActions[name].fetch,
  Object.fromEntries(Object.entries(PAGE_ACTIONS).map(([handler, action]) => [handler, typeOf(name, action)])),
  withQuery(name)
))));

//
// Reducers

export const reducers = createHandleActions(Object.fromEntries(LIST_NAMES.map((name) => [
  typeOf(name, 'setTableOption'),
  createSetTableOptionReducer(sectionOf(name))
])), defaultState, section);
