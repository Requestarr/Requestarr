import { createAction } from 'redux-actions';
import { filterBuilderTypes, filterBuilderValueTypes, sortDirections } from 'Helpers/Props';
import { contentTypeText, statusText } from 'Portal/portalText';
import translate from 'Utilities/String/translate';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';
import { filterPredicates, filters, peopleOf, sortPredicates, uniqueBy } from './requestActions';

// How the Requests page shows them: sort, filters, custom filters, view, columns and page size, remembered in this
// browser. Sonarr main Store/Actions/seriesIndexActions.js; Prowlarr Store/Actions/indexerIndexActions.js.

//
// Variables

export const section = 'requestIndex';

//
// State

export const defaultState = {
  sortKey: 'requested',
  sortDirection: sortDirections.DESCENDING,
  secondarySortKey: 'title',
  secondarySortDirection: sortDirections.ASCENDING,
  view: 'posters',
  pageSize: 25,

  columns: [
    { name: 'poster', columnLabel: () => translate('PortalPoster'), isSortable: false, isVisible: true },
    { name: 'title', label: () => translate('PortalTitle'), isSortable: true, isVisible: true, isModifiable: false },
    { name: 'type', label: () => translate('PortalType'), isSortable: true, isVisible: true },
    { name: 'status', label: () => translate('PortalStatus'), isSortable: true, isVisible: true },
    { name: 'requester', label: () => translate('PortalRequester'), isSortable: true, isVisible: true },
    { name: 'followers', label: () => translate('PortalFollowers'), isSortable: true, isVisible: true },
    { name: 'instance', label: () => translate('PortalInstance'), isSortable: true, isVisible: true },
    { name: 'requested', label: () => translate('PortalRequested'), isSortable: true, isVisible: true },
    { name: 'decided', label: () => translate('PortalDecided'), isSortable: true, isVisible: false },
    { name: 'fulfilled', label: () => translate('PortalFulfilled'), isSortable: true, isVisible: false },
    { name: 'actions', columnLabel: () => translate('Actions'), isVisible: true, isModifiable: false }
  ],

  sortPredicates,

  selectedFilterKey: 'all',
  filters,
  filterPredicates,

  filterBuilderProps: [
    {
      name: 'title',
      label: () => translate('PortalTitle'),
      type: filterBuilderTypes.STRING
    },
    {
      name: 'contentType',
      label: () => translate('PortalType'),
      type: filterBuilderTypes.EXACT,
      optionsSelector: (items) => uniqueBy(items, (r) => ({ id: r.contentType, name: contentTypeText(r.contentType) }))
    },
    {
      name: 'status',
      label: () => translate('PortalStatus'),
      type: filterBuilderTypes.EXACT,
      optionsSelector: (items) => uniqueBy(items, (r) => ({ id: r.status, name: statusText(r.status) }))
    },
    {
      name: 'member',
      label: () => translate('PortalMember'),
      type: filterBuilderTypes.EXACT,
      optionsSelector: (items) => uniqueBy(items.flatMap(peopleOf), (p) => ({ id: p.id, name: p.name ?? String(p.id) }))
    },
    {
      name: 'instanceId',
      label: () => translate('PortalInstance'),
      type: filterBuilderTypes.EXACT,
      optionsSelector: (items) => uniqueBy(items, (r) => (r.instanceId == null ? null : { id: r.instanceId, name: r.instance }))
    },
    {
      name: 'requested',
      label: () => translate('PortalRequested'),
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'decided',
      label: () => translate('PortalDecided'),
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'fulfilled',
      label: () => translate('PortalFulfilled'),
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    }
  ]
};

export const persistState = [
  'requestIndex.sortKey',
  'requestIndex.sortDirection',
  'requestIndex.selectedFilterKey',
  'requestIndex.view',
  'requestIndex.columns',
  'requestIndex.pageSize'
];

//
// Actions Types

export const SET_REQUEST_SORT = 'requestIndex/setRequestSort';
export const SET_REQUEST_FILTER = 'requestIndex/setRequestFilter';
export const SET_REQUEST_VIEW = 'requestIndex/setRequestView';
export const SET_REQUEST_TABLE_OPTION = 'requestIndex/setRequestTableOption';

//
// Action Creators

export const setRequestSort = createAction(SET_REQUEST_SORT);
export const setRequestFilter = createAction(SET_REQUEST_FILTER);
export const setRequestView = createAction(SET_REQUEST_VIEW);
export const setRequestTableOption = createAction(SET_REQUEST_TABLE_OPTION);

//
// Reducers

export const reducers = createHandleActions({
  [SET_REQUEST_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_REQUEST_FILTER]: createSetClientSideCollectionFilterReducer(section),

  [SET_REQUEST_VIEW]: function(state, { payload }) {
    return Object.assign({}, state, { view: payload.view });
  },

  [SET_REQUEST_TABLE_OPTION]: createSetTableOptionReducer(section)
}, defaultState, section);
