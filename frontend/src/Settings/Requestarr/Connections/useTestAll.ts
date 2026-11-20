import { useCallback, useState } from 'react';
import { callRequestarrApi } from '../requestarrApi';
import { Instance, InstanceTestResult } from '../RequestarrSettingsTypes';

// One instance tested with its saved settings and key: why it failed, prefixed with its name, or nothing.
function testOne(instance: Instance) {
  const name = instance.settings.name;

  return callRequestarrApi<InstanceTestResult>(
    `instances/test?id=${instance.id}`,
    'POST',
    instance.settings
  ).then(
    (result) => (result.problem ? `${name}: ${result.problem}` : undefined),
    (failure: Error) => `${name}: ${failure.message}`
  );
}

// Test All Connections: every Sonarr and Radarr that's turned on, at once. Problems is what failed, empty when
// everything passed, and undefined until a test has run.
function useTestAll(instances: Instance[] | undefined, onDone: () => void) {
  const [isTesting, setIsTesting] = useState(false);
  const [problems, setProblems] = useState<string[]>();

  const onTestAll = useCallback(() => {
    const turnedOn = (instances ?? []).filter(
      (instance) => instance.settings.enabled
    );

    setIsTesting(true);

    Promise.all(turnedOn.map(testOne)).then((found) => {
      setProblems(found.filter((problem): problem is string => !!problem));
      setIsTesting(false);
      onDone();
    });
  }, [instances, onDone]);

  return { isTesting, problems, onTestAll };
}

export default useTestAll;
