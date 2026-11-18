import { useCallback, useState } from 'react';
import { callRequestarrApi } from './requestarrApi';
import { AppSettings, SettingValue } from './RequestarrSettingsTypes';
import useRequestarrApi from './useRequestarrApi';

interface SettingChange {
  name: string;
  value: SettingValue;
}

// Requestarr's settings and the changes made on the page; saving sends only those changes.
function useSettingsForm(onSaved?: () => void) {
  const { data, error, setData } = useRequestarrApi<AppSettings>('settings');
  const [pending, setPending] = useState<Record<string, SettingValue>>({});
  const [isSaving, setIsSaving] = useState(false);
  const [saveError, setSaveError] = useState<string>();

  const onChange = useCallback(({ name, value }: SettingChange) => {
    setPending((current) => ({ ...current, [name]: value }));
  }, []);

  const onSave = useCallback(() => {
    setIsSaving(true);
    setSaveError(undefined);

    callRequestarrApi<AppSettings>('settings', 'PUT', pending).then(
      (saved) => {
        setData(saved);
        setPending({});
        setIsSaving(false);
        onSaved?.();
      },
      (failure: Error) => {
        setSaveError(failure.message);
        setIsSaving(false);
      }
    );
  }, [pending, setData, onSaved]);

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
  };
}

export type SettingsForm = ReturnType<typeof useSettingsForm>;

export default useSettingsForm;
