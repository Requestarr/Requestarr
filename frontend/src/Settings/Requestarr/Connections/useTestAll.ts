import { useCallback, useState } from 'react';
import { callRequestarrApi } from '../requestarrApi';
import { Instance, InstanceTestResult } from '../RequestarrSettingsTypes';

// What testing one instance found: why it failed, or no problem when it passed.
export interface TestOutcome {
  id: number;
  name: string;
  problem?: string;
}

// One instance tested with its saved settings and key.
function testOne(instance: Instance): Promise<TestOutcome> {
  const { id, settings } = instance;

  return callRequestarrApi<InstanceTestResult>(
    `instances/test?id=${id}`,
    'POST',
    settings
  ).then(
    (result) => ({ id, name: settings.name, problem: result.problem }),
    (failure: Error) => ({ id, name: settings.name, problem: failure.message })
  );
}

// Test All Connections: every Sonarr and Radarr that's turned on, at once. Outcomes is one per instance tested (empty
// when none is turned on), and undefined until a test has run; the cards show them in place of the last check.
function useTestAll(instances: Instance[] | undefined, onDone: () => void) {
  const [isTesting, setIsTesting] = useState(false);
  const [outcomes, setOutcomes] = useState<TestOutcome[]>();

  const onTestAll = useCallback(() => {
    const turnedOn = (instances ?? []).filter(
      (instance) => instance.settings.enabled
    );

    setIsTesting(true);

    Promise.all(turnedOn.map(testOne)).then((found) => {
      setOutcomes(found);
      setIsTesting(false);
      onDone();
    });
  }, [instances, onDone]);

  return { isTesting, outcomes, onTestAll };
}

export default useTestAll;
