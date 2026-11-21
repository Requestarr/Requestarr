import { createAction } from 'redux-actions';
import { sortDirections } from 'Helpers/Props';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createFetchSchemaHandler from 'Store/Actions/Creators/createFetchSchemaHandler';
import createSaveProviderHandler, { createCancelSaveProviderHandler } from 'Store/Actions/Creators/createSaveProviderHandler';
import createTestProviderHandler, { createCancelTestProviderHandler } from 'Store/Actions/Creators/createTestProviderHandler';
import createSetProviderFieldValueReducer from 'Store/Actions/Creators/Reducers/createSetProviderFieldValueReducer';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';
import createAjaxRequest from 'Utilities/createAjaxRequest';
import selectProviderSchema from 'Utilities/State/selectProviderSchema';
import { set } from '../baseActions';

// Requestarr's Sonarr and Radarr instances, as Prowlarr's Store/Actions/Settings/applications.js keeps its
// applications: Servarr's provider handlers at /mediamanager. An instance is never deleted (it's turned off instead)
// and has no tags, so there's no delete or bulk edit.

//
// Variables

const section = 'settings.mediaManagers';
const url = '/mediamanager';

//
// Actions Types

export const FETCH_MEDIA_MANAGERS = 'settings/mediaManagers/fetchMediaManagers';
export const FETCH_MEDIA_MANAGER_SCHEMA = 'settings/mediaManagers/fetchMediaManagerSchema';
export const SELECT_MEDIA_MANAGER_SCHEMA = 'settings/mediaManagers/selectMediaManagerSchema';
export const SET_MEDIA_MANAGER_VALUE = 'settings/mediaManagers/setMediaManagerValue';
export const SET_MEDIA_MANAGER_FIELD_VALUE = 'settings/mediaManagers/setMediaManagerFieldValue';
export const SAVE_MEDIA_MANAGER = 'settings/mediaManagers/saveMediaManager';
export const CANCEL_SAVE_MEDIA_MANAGER = 'settings/mediaManagers/cancelSaveMediaManager';
export const TEST_MEDIA_MANAGER = 'settings/mediaManagers/testMediaManager';
export const CANCEL_TEST_MEDIA_MANAGER = 'settings/mediaManagers/cancelTestMediaManager';
export const TEST_ALL_MEDIA_MANAGERS = 'settings/mediaManagers/testAllMediaManagers';

//
// Action Creators

export const fetchMediaManagers = createThunk(FETCH_MEDIA_MANAGERS);
export const fetchMediaManagerSchema = createThunk(FETCH_MEDIA_MANAGER_SCHEMA);
export const selectMediaManagerSchema = createAction(SELECT_MEDIA_MANAGER_SCHEMA);

export const saveMediaManager = createThunk(SAVE_MEDIA_MANAGER);
export const cancelSaveMediaManager = createThunk(CANCEL_SAVE_MEDIA_MANAGER);
export const testMediaManager = createThunk(TEST_MEDIA_MANAGER);
export const cancelTestMediaManager = createThunk(CANCEL_TEST_MEDIA_MANAGER);
export const testAllMediaManagers = createThunk(TEST_ALL_MEDIA_MANAGERS);

export const setMediaManagerValue = createAction(SET_MEDIA_MANAGER_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

export const setMediaManagerFieldValue = createAction(SET_MEDIA_MANAGER_FIELD_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

// Servarr's Test All (createTestAllProvidersHandler), keeping what it found: one result per instance tested, each with
// its validation failures, so the page can say which failed and why.
function testAllHandler(getState, payload, dispatch) {
  dispatch(set({ section, isTestingAll: true }));

  const { request } = createAjaxRequest({
    url: `${url}/testall`,
    method: 'POST',
    contentType: 'application/json',
    dataType: 'json'
  });

  request.done((data) => {
    dispatch(set({ section, isTestingAll: false, testAllResults: data }));
  });

  request.fail((xhr) => {
    dispatch(set({ section, isTestingAll: false, testAllResults: xhr.responseJSON ?? [] }));
  });
}

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    isSchemaFetching: false,
    isSchemaPopulated: false,
    schemaError: null,
    schema: [],
    selectedSchema: {},
    isSaving: false,
    saveError: null,
    isTesting: false,
    isTestingAll: false,
    testAllResults: null,
    items: [],
    pendingChanges: {},
    sortKey: 'name',
    sortDirection: sortDirections.ASCENDING
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_MEDIA_MANAGERS]: createFetchHandler(section, url),
    [FETCH_MEDIA_MANAGER_SCHEMA]: createFetchSchemaHandler(section, `${url}/schema`),

    [SAVE_MEDIA_MANAGER]: createSaveProviderHandler(section, url),
    [CANCEL_SAVE_MEDIA_MANAGER]: createCancelSaveProviderHandler(section),
    [TEST_MEDIA_MANAGER]: createTestProviderHandler(section, url),
    [CANCEL_TEST_MEDIA_MANAGER]: createCancelTestProviderHandler(section),
    [TEST_ALL_MEDIA_MANAGERS]: testAllHandler
  },

  //
  // Reducers

  reducers: {
    [SET_MEDIA_MANAGER_VALUE]: createSetSettingValueReducer(section),
    [SET_MEDIA_MANAGER_FIELD_VALUE]: createSetProviderFieldValueReducer(section),

    [SELECT_MEDIA_MANAGER_SCHEMA]: (state, { payload }) => {
      return selectProviderSchema(state, section, payload, (selectedSchema) => {
        selectedSchema.name = selectedSchema.implementationName;
        selectedSchema.enable = true;

        return selectedSchema;
      });
    }
  }

};
