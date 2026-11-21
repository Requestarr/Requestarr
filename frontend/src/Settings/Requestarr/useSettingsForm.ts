import { useCallback, useEffect, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AnyAction } from 'redux';
import AppState from 'App/State/AppState';
import usePrevious from 'Helpers/Hooks/usePrevious';
import {
  fetchRequestarrSettings,
  saveRequestarrSettings,
  setRequestarrSettingsValue,
} from 'Store/Actions/settingsActions';
import { AppSettings, SettingValue } from './RequestarrSettingsTypes';

interface SettingChange {
  name: string;
  value: SettingValue;
}

// Servarr's setting value action, typed: its payload creator is JavaScript.
const setValue = setRequestarrSettingsValue as unknown as (
  change: SettingChange
) => AnyAction;

interface RequestarrSettingsSection {
  isFetching: boolean;
  isPopulated: boolean;
  error?: { responseJSON?: { message?: string } } | null;
  isSaving: boolean;
  saveError?: { responseJSON?: { message?: string } } | null;
  pendingChanges: Record<string, SettingValue>;
  item: Record<string, SettingValue>;
}

// Requestarr's answer for a request that failed, in its own words when it gave some.
function problemOf(failure: RequestarrSettingsSection['error']) {
  return failure ? failure.responseJSON?.message ?? String(failure) : undefined;
}

// The item's settings apart from whether the provisioning file sets them.
function settingsOf(
  section: RequestarrSettingsSection
): AppSettings | undefined {
  if (!section.isPopulated) {
    return undefined;
  }

  const { provisioned, ...values } = section.item;

  return { provisioned: !!provisioned, values };
}

// Requestarr's settings and the changes made on the page, in the settings.requestarr store section as Servarr's
// general settings are (Settings/General): Servarr's fetch and save handlers, the page's changes as its pending
// changes. Saving sends the whole item; a secret left as shown, or emptied, keeps the saved one. onSaved runs after
// each save that worked.
function useSettingsForm(onSaved?: () => void) {
  const dispatch = useDispatch();
  const section = useSelector(
    (state: AppState) =>
      (state.settings as unknown as { requestarr: RequestarrSettingsSection })
        .requestarr
  );
  const wasSaving = usePrevious(section.isSaving);

  const reload = useCallback(
    () => dispatch(fetchRequestarrSettings()),
    [dispatch]
  );

  useEffect(() => {
    reload();
  }, [reload]);

  useEffect(() => {
    if (wasSaving && !section.isSaving && !section.saveError) {
      onSaved?.();
    }
  }, [wasSaving, section.isSaving, section.saveError, onSaved]);

  const onChange = useCallback(
    ({ name, value }: SettingChange) => dispatch(setValue({ name, value })),
    [dispatch]
  );
  const onSave = useCallback(
    () => dispatch(saveRequestarrSettings()),
    [dispatch]
  );
  const settings = useMemo(() => settingsOf(section), [section]);

  return {
    settings,
    error: problemOf(section.error),
    pending: section.pendingChanges,
    hasChanges: Object.keys(section.pendingChanges).length > 0,
    isSaving: section.isSaving,
    saveError: problemOf(section.saveError),
    onChange,
    onSave,
    reload,
  };
}

export type SettingsForm = ReturnType<typeof useSettingsForm>;

export default useSettingsForm;
