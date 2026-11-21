import { createAction } from 'redux-actions';
import { filterBuilderTypes, filterBuilderValueTypes, sortDirections } from 'Helpers/Props';
import { contentTypeText, memberStatusText } from 'Portal/portalText';
import translate from 'Utilities/String/translate';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';
import { ACCESS_TYPES, filterPredicates, filters, sortPredicates } from './memberActions';
import { uniqueBy } from './requestActions';

// How the Members page shows them: sort, filters, custom filters and columns, remembered in this browser.
// Prowlarr Store/Actions/indexerIndexActions.js.

//
// Variables

export const section = 'memberIndex';

function column(name, label, isVisible = true) {
  return { name, label, isSortable: true, isVisible };
}

function dateProp(name, label) {
  return { name, label, type: filterBuilderTypes.DATE, valueType: filterBuilderValueTypes.DATE };
}

//
// State

export const defaultState = {
  sortKey: 'name',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'name',
  secondarySortDirection: sortDirections.ASCENDING,

  columns: [
    { ...column('name', () => translate('PortalMember')), isModifiable: false },
    column('status', () => translate('PortalStatus')),
    ...ACCESS_TYPES.map((type) => column(`access-${type}`, () => contentTypeText(type), type !== 'music')),
    column('requests', () => translate('PortalRequests')),
    column('active', () => translate('PortalActive')),
    column('pending', () => translate('PortalPending')),
    column('following', () => translate('PortalFollowing')),
    column('byType', () => translate('PortalByType')),
    column('firstRequest', () => translate('PortalFirstRequest'), false),
    column('lastRequest', () => translate('PortalLastRequest')),
    column('added', () => translate('PortalAdded'))
  ],

  sortPredicates,

  selectedFilterKey: 'all',
  filters,
  filterPredicates,

  filterBuilderProps: [
    { name: 'name', label: () => translate('PortalMember'), type: filterBuilderTypes.STRING },
    {
      name: 'status',
      label: () => translate('PortalStatus'),
      type: filterBuilderTypes.EXACT,
      optionsSelector: (items) => uniqueBy(items, (m) => ({ id: m.status, name: memberStatusText(m.status) }))
    },
    { name: 'requests', label: () => translate('PortalRequests'), type: filterBuilderTypes.NUMBER },
    { name: 'active', label: () => translate('PortalActive'), type: filterBuilderTypes.NUMBER },
    { name: 'pending', label: () => translate('PortalPending'), type: filterBuilderTypes.NUMBER },
    dateProp('added', () => translate('PortalAdded')),
    dateProp('firstRequest', () => translate('PortalFirstRequest')),
    dateProp('lastRequest', () => translate('PortalLastRequest'))
  ]
};

export const persistState = [
  'memberIndex.sortKey',
  'memberIndex.sortDirection',
  'memberIndex.selectedFilterKey',
  'memberIndex.columns'
];

//
// Actions Types

export const SET_MEMBER_SORT = 'memberIndex/setMemberSort';
export const SET_MEMBER_FILTER = 'memberIndex/setMemberFilter';
export const SET_MEMBER_TABLE_OPTION = 'memberIndex/setMemberTableOption';

//
// Action Creators

export const setMemberSort = createAction(SET_MEMBER_SORT);
export const setMemberFilter = createAction(SET_MEMBER_FILTER);
export const setMemberTableOption = createAction(SET_MEMBER_TABLE_OPTION);

//
// Reducers

export const reducers = createHandleActions({
  [SET_MEMBER_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_MEMBER_FILTER]: createSetClientSideCollectionFilterReducer(section),
  [SET_MEMBER_TABLE_OPTION]: createSetTableOptionReducer(section)
}, defaultState, section);
