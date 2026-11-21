import { createAction } from 'redux-actions';
import { handleThunks } from 'Store/thunks';
import createHandleActions from './Creators/createHandleActions';
import development from './Settings/development';
import general from './Settings/general';
import mediaManagers from './Settings/mediaManagers';
import requestarr from './Settings/requestarr';
import ui from './Settings/ui';

export * from './Settings/general';
export * from './Settings/mediaManagers';
export * from './Settings/requestarr';
export * from './Settings/development';
export * from './Settings/ui';

//
// Variables

export const section = 'settings';

//
// State

export const defaultState = {
  advancedSettings: false,

  general: general.defaultState,
  mediaManagers: mediaManagers.defaultState,
  requestarr: requestarr.defaultState,
  development: development.defaultState,
  ui: ui.defaultState
};

export const persistState = [
  'settings.advancedSettings'
];

//
// Actions Types

export const TOGGLE_ADVANCED_SETTINGS = 'settings/toggleAdvancedSettings';

//
// Action Creators

export const toggleAdvancedSettings = createAction(TOGGLE_ADVANCED_SETTINGS);

//
// Action Handlers

export const actionHandlers = handleThunks({
  ...general.actionHandlers,
  ...mediaManagers.actionHandlers,
  ...requestarr.actionHandlers,
  ...development.actionHandlers,
  ...ui.actionHandlers
});

//
// Reducers

export const reducers = createHandleActions({

  [TOGGLE_ADVANCED_SETTINGS]: (state, { payload }) => {
    return Object.assign({}, state, { advancedSettings: !state.advancedSettings });
  },

  ...general.reducers,
  ...mediaManagers.reducers,
  ...requestarr.reducers,
  ...development.reducers,
  ...ui.reducers

}, defaultState, section);
