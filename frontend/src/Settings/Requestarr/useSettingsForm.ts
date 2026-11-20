import { useCallback, useState } from 'react';
import { callRequestarrApi } from './requestarrApi';
import { AppSettings, SettingValue } from './RequestarrSettingsTypes';
import useApiAction from './useApiAction';
import useRequestarrApi from './useRequestarrApi';

interface SettingChange {
  name: string;
  value: SettingValue;
}

// A secret's field starts empty and its saved value only says whether one is set: a field typed in and emptied again
// keeps the saved secret instead of erasing it.
function changesToSend(
  pending: Record<string, SettingValue>,
  settings: AppSettings | undefined
) {
  return Object.fromEntries(
    Object.entries(pending).filter(
      ([name, value]) =>
        !(value === '' && typeof settings?.values[name] === 'boolean')
    )
  );
}

// Requestarr's settings and the changes made on the page; saving sends only those changes.
function useSettingsForm(onSaved?: () => void) {
  const { data, error, reload, setData } =
    useRequestarrApi<AppSettings>('settings');
  const [pending, setPending] = useState<Record<string, SettingValue>>({});
  const { isBusy: isSaving, problem: saveError, run } = useApiAction();

  const onChange = useCallback(({ name, value }: SettingChange) => {
    setPending((current) => ({ ...current, [name]: value }));
  }, []);

  const onSave = useCallback(() => {
    run(
      () =>
        callRequestarrApi<AppSettings>(
          'settings',
          'PUT',
          changesToSend(pending, data)
        ),
      (saved) => {
        setData(saved);
        setPending({});
        onSaved?.();
      }
    );
  }, [pending, data, run, setData, onSaved]);

  const hasChanges = Object.keys(pending).length > 0;

  return {
    settings: data,
    error,
    pending,
    hasChanges,
    isSaving,
    saveError,
    onChange,
    onSave,
    reload,
  };
}

export type SettingsForm = ReturnType<typeof useSettingsForm>;

export default useSettingsForm;
