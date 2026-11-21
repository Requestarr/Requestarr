import AppSectionState, {
  AppSectionItemState,
  AppSectionSaveState,
} from 'App/State/AppSectionState';
import MediaManager, { TestAllResult } from 'typings/MediaManager';
import General from 'typings/Settings/General';
import UiSettings from 'typings/Settings/UiSettings';

export interface GeneralAppState
  extends AppSectionItemState<General>,
    AppSectionSaveState {}

// Servarr's provider section, as Prowlarr's applications: the instances, the schema to add one from, and Test All.
interface MediaManagerAppState
  extends AppSectionState<MediaManager>,
    AppSectionSaveState {
  isSchemaFetching: boolean;
  isSchemaPopulated: boolean;
  schema: MediaManager[];
  selectedSchema: Partial<MediaManager>;
  isTesting: boolean;
  isTestingAll: boolean;
  testAllResults: TestAllResult[] | null;
}

export type UiSettingsAppState = AppSectionItemState<UiSettings>;

interface SettingsAppState {
  advancedSettings: boolean;
  general: GeneralAppState;
  mediaManagers: MediaManagerAppState;
  ui: UiSettingsAppState;
}

export default SettingsAppState;
