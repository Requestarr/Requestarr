import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import createHandleActions from './Creators/createHandleActions';
import createSetClientSideCollectionSortReducer from './Creators/Reducers/createSetClientSideCollectionSortReducer';
import createSetTableOptionReducer from './Creators/Reducers/createSetTableOptionReducer';

// Import → From Requestrr's preview: the rows it would import, with their sort and columns remembered in this
// browser, as Sonarr's client-side lists keep theirs (Store/Actions/seriesIndexActions.js).

//
// Variables

export const section = 'importPreview';

function column(name, label) {
  return { name, label, isSortable: true, isVisible: true };
}

//
// State

export const defaultState = {
  items: [],
  sortKey: 'when',
  sortDirection: sortDirections.ASCENDING,
  secondarySortKey: 'title',
  secondarySortDirection: sortDirections.ASCENDING,

  columns: [
    { ...column('title', () => translate('PortalTitle')), isModifiable: false },
    column('type', () => translate('PortalType')),
    column('requester', () => translate('PortalRequester')),
    column('followers', () => translate('PortalFollowers')),
    column('when', () => translate('PortalWhen')),
    column('ended', () => translate('ImportInRequestrr')),
    column('inLibrary', () => translate('ImportInLibrary'))
  ]
};

export const persistState = [
  'importPreview.sortKey',
  'importPreview.sortDirection',
  'importPreview.columns'
];

//
// Actions Types

export const SET_IMPORT_PREVIEW_SORT = 'importPreview/setImportPreviewSort';
export const SET_IMPORT_PREVIEW_TABLE_OPTION = 'importPreview/setImportPreviewTableOption';

//
// Action Creators

export const setImportPreviewSort = createAction(SET_IMPORT_PREVIEW_SORT);
export const setImportPreviewTableOption = createAction(SET_IMPORT_PREVIEW_TABLE_OPTION);

//
// Reducers

export const reducers = createHandleActions({
  [SET_IMPORT_PREVIEW_SORT]: createSetClientSideCollectionSortReducer(section),
  [SET_IMPORT_PREVIEW_TABLE_OPTION]: createSetTableOptionReducer(section)
}, defaultState, section);
