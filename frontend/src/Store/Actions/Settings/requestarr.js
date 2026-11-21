import { createAction } from 'redux-actions';
import createFetchHandler from 'Store/Actions/Creators/createFetchHandler';
import createSaveHandler from 'Store/Actions/Creators/createSaveHandler';
import createSetSettingValueReducer from 'Store/Actions/Creators/Reducers/createSetSettingValueReducer';
import { createThunk } from 'Store/thunks';

// Requestarr's own settings (removal votes, access requests, the spam guard, the Discord bot and the rest), read and
// saved whole as Servarr's general settings are (Store/Actions/Settings/general.js), at /config/requestarr.

//
// Variables

const section = 'settings.requestarr';

//
// Actions Types

export const FETCH_REQUESTARR_SETTINGS = 'settings.requestarr/fetchRequestarrSettings';
export const SET_REQUESTARR_SETTINGS_VALUE = 'settings.requestarr/setRequestarrSettingsValue';
export const SAVE_REQUESTARR_SETTINGS = 'settings.requestarr/saveRequestarrSettings';

//
// Action Creators

export const fetchRequestarrSettings = createThunk(FETCH_REQUESTARR_SETTINGS);
export const saveRequestarrSettings = createThunk(SAVE_REQUESTARR_SETTINGS);
export const setRequestarrSettingsValue = createAction(SET_REQUESTARR_SETTINGS_VALUE, (payload) => {
  return {
    section,
    ...payload
  };
});

//
// Details

export default {

  //
  // State

  defaultState: {
    isFetching: false,
    isPopulated: false,
    error: null,
    pendingChanges: {},
    isSaving: false,
    saveError: null,
    item: {}
  },

  //
  // Action Handlers

  actionHandlers: {
    [FETCH_REQUESTARR_SETTINGS]: createFetchHandler(section, '/config/requestarr'),
    [SAVE_REQUESTARR_SETTINGS]: createSaveHandler(section, '/config/requestarr')
  },

  //
  // Reducers

  reducers: {
    [SET_REQUESTARR_SETTINGS_VALUE]: createSetSettingValueReducer(section)
  }

};
