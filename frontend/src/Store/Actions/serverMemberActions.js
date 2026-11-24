import { createAction } from 'redux-actions';
import { filterBuilderTypes, filterBuilderValueTypes, filterTypes, sortDirections } from 'Helpers/Props';
import { createThunk, handleThunks } from 'Store/thunks';
import dateFilterPredicate from 'Utilities/Date/dateFilterPredicate';
import translate from 'Utilities/String/translate';
import createFetchHandler from './Creators/createFetchHandler';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionFilterReducer from './Creators/Reducers/createSetClientSideCollectionFilterReducer';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

// Everyone in the Discord server who isn't onboarded, and how the Onboard page lists them: sort, filters, custom filters
// and columns, remembered in this browser. Prowlarr Store/Actions/releaseActions.js.

//
// Variables

export const section = 'serverMembers';

function column(name, label, isSortable = true) {
  return { name, label: () => translate(label), isSortable, isVisible: true };
}

//
// State

export const defaultState = {
  isFetching: false,
  isPopulated: false,
  error: null,
  items: [],

  sortKey: 'importedRequests',
  sortDirection: sortDirections.DESCENDING,
  secondarySortKey: 'name',
  secondarySortDirection: sortDirections.ASCENDING,

  columns: [
    { ...column('name', 'PortalMember'), isModifiable: false },
    column('joined', 'PortalJoinedServer'),
    column('roles', 'PortalServerRoles', false),
    column('importedRequests', 'PortalImportedHistory')
  ],

  sortPredicates: {
    name: (item) => item.name.toLowerCase()
  },

  selectedFilterKey: 'all',

  filters: [
    { key: 'all', label: () => translate('All'), filters: [] },
    {
      key: 'imported',
      label: () => translate('PortalImportedHistory'),
      filters: [{ key: 'importedRequests', value: 0, type: filterTypes.GREATER_THAN }]
    }
  ],

  filterPredicates: {
    joined: (item, value, type) => dateFilterPredicate(item.joined, value, type)
  },

  filterBuilderProps: [
    { name: 'name', label: () => translate('PortalMember'), type: filterBuilderTypes.STRING },
    { name: 'username', label: () => translate('PortalDiscordUsername'), type: filterBuilderTypes.STRING },
    {
      name: 'joined',
      label: () => translate('PortalJoinedServer'),
      type: filterBuilderTypes.DATE,
      valueType: filterBuilderValueTypes.DATE
    },
    {
      name: 'roles',
      label: () => translate('PortalServerRoles'),
      type: filterBuilderTypes.ARRAY,
      optionsSelector: (items) => [...new Set(items.flatMap((item) => item.roles))].sort().map((role) => ({ id: role, name: role }))
    },
    { name: 'importedRequests', label: () => translate('PortalImportedHistory'), type: filterBuilderTypes.NUMBER }
  ]
};

export const persistState = [
  'serverMembers.sortKey',
  'serverMembers.sortDirection',
  'serverMembers.selectedFilterKey',
  'serverMembers.columns'
];

//
// Actions Types

export const FETCH_SERVER_MEMBERS = 'serverMembers/fetchServerMembers';
export const SET_SERVER_MEMBER_SORT = 'serverMembers/setServerMemberSort';
export const SET_SERVER_MEMBER_FILTER = 'serverMembers/setServerMemberFilter';
export const SET_SERVER_MEMBER_TABLE_OPTION = 'serverMembers/setServerMemberTableOption';

//
// Action Creators

export const fetchServerMembers = createThunk(FETCH_SERVER_MEMBERS);
export const setServerMemberSort = createAction(SET_SERVER_MEMBER_SORT);
export const setServerMemberFilter = createAction(SET_SERVER_MEMBER_FILTER);
export const setServerMemberTableOption = createAction(SET_SERVER_MEMBER_TABLE_OPTION);

//
// Action Handlers

export const actionHandlers = handleThunks({
  [FETCH_SERVER_MEMBERS]: createFetchHandler(section, '/portal/server-members')
});

//
// Reducers

export const reducers = createHandleActions({
  [SET_SERVER_MEMBER_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_SERVER_MEMBER_FILTER]: createSetClientSideCollectionFilterReducer(section),
  [SET_SERVER_MEMBER_TABLE_OPTION]: createSetTableOptionReducer(section)
}, defaultState, section);
