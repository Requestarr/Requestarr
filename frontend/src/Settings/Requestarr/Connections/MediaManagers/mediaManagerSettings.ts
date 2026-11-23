import { useMemo } from 'react';
import { useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { ProviderField } from 'typings/MediaManager';

// The media managers' provider section in the store (Store/Actions/Settings/mediaManagers.js).
export const MEDIA_MANAGERS_SECTION = 'settings.mediaManagers';

// A setting as Servarr's selectSettings gives it: its value, with what validation found.
interface Setting<T> {
  value: T;
  errors: object[];
  warnings: object[];
}

// How a saved instance tells Requestarr about downloads (InstanceUpdates on the server).
export interface InstanceUpdates {
  rightAway: boolean;
  reason:
    | 'rightAway'
    | 'noAddress'
    | 'unreachable'
    | 'setByFile'
    | 'notChecked';
  pollMinutes: number;
}

export interface MediaManagerItem {
  id?: Setting<number>;
  name: Setting<string>;
  enable: Setting<boolean>;
  implementation: Setting<string>;
  implementationName: string;
  updates?: Setting<InstanceUpdates | null>;
  fields: (ProviderField & { errors: object[]; pending: boolean })[];
}

export interface MediaManagerSettings {
  isFetching: boolean;
  error?: object;
  isSaving: boolean;
  saveError?: object | null;
  isTesting: boolean;
  validationErrors: object[];
  validationWarnings: object[];
  hasPendingChanges: boolean;
  item: MediaManagerItem;
}

// The instance being edited, or the new one picked from the schema (no id), with its changes, as Servarr's provider
// modals read it (createProviderSettingsSelector).
export function useMediaManagerSettings(id?: number): MediaManagerSettings {
  const selector = useMemo(
    () => createProviderSettingsSelector('mediaManagers'),
    []
  );

  return useSelector(
    (state: AppState) =>
      selector(state, { id }) as unknown as MediaManagerSettings
  );
}

// Sonarr's and Radarr's usual ports, for the address's example.
const PORTS: Record<string, number> = { Sonarr: 8989, Radarr: 7878 };

export function portOf(app: string) {
  return PORTS[app] ?? 8989;
}
