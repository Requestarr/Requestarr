import { useCallback, useState } from 'react';
import { callRequestarrApi } from './requestarrApi';
import {
  Instance,
  InstanceSettings,
  InstanceTestResult,
  SettingValue,
} from './RequestarrSettingsTypes';
import useApiAction from './useApiAction';

const NEW_INSTANCE = {
  name: '',
  url: '',
  seriesType: 'standard',
  isDefault: true,
  enabled: true,
  pollingOnly: false,
  animeEnabled: true,
  animePriority: 'dub',
};

// A Sonarr or Radarr being added or changed: Test reaches it and lists its quality profiles and root folders; Save
// keeps it. An existing one keeps its saved API key unless a new one is typed.
function useInstanceForm(
  instance: Instance | undefined,
  implementation: string,
  onSaved: (saved: Instance) => void
) {
  const [values, setValues] = useState<InstanceSettings>(
    instance?.settings ?? { ...NEW_INSTANCE, implementation }
  );
  const [test, setTest] = useState<InstanceTestResult>();
  const { isBusy, problem, setProblem, run } = useApiAction();

  const onChange = useCallback(
    ({ name, value }: { name: string; value: SettingValue }) => {
      setValues((current) => ({ ...current, [name]: value }));
    },
    []
  );

  const onTest = useCallback(() => {
    const query = instance ? `?id=${instance.id}` : '';

    run(
      () =>
        callRequestarrApi<InstanceTestResult>(
          `instances/test${query}`,
          'POST',
          values
        ),
      (result) => {
        setTest(result);
        setProblem(result.problem);
      }
    );
  }, [instance, values, run, setProblem]);

  const onSave = useCallback(() => {
    const path = instance ? `instances/${instance.id}` : 'instances';

    run(
      () =>
        callRequestarrApi<Instance>(path, instance ? 'PUT' : 'POST', values),
      onSaved
    );
  }, [instance, values, run, onSaved]);

  return {
    values,
    test,
    problem,
    isBusy,
    apiKeySet: instance?.apiKeySet ?? false,
    onChange,
    onTest,
    onSave,
  };
}

export type InstanceForm = ReturnType<typeof useInstanceForm>;

export default useInstanceForm;
