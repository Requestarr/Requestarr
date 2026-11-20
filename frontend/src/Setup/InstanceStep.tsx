import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import InstanceFields from 'Settings/Requestarr/InstanceFields';
import { callRequestarrApi } from 'Settings/Requestarr/requestarrApi';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
import useApiAction from 'Settings/Requestarr/useApiAction';
import useInstanceForm from 'Settings/Requestarr/useInstanceForm';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './SetupPage.css';

interface NewInstanceProps {
  implementation: string;
  onSaved: () => void;
}

function NewInstance({ implementation, onSaved }: NewInstanceProps) {
  const form = useInstanceForm(undefined, implementation, onSaved);

  return (
    <>
      <InstanceFields form={form} isReadOnly={false} />

      <SpinnerButton
        kind={kinds.PRIMARY}
        isSpinning={form.isBusy}
        onPress={form.onSave}
      >
        {translate('Save')}
      </SpinnerButton>
    </>
  );
}

interface InstanceStepProps {
  setup: SetupStatus;
  onSaved: () => void;
}

// The first Sonarr or Radarr: pick which, fill in its address and API key, Test, choose what requests use, Save. Skip,
// there until the step is done, leaves it for later: Settings, Connections adds them with the Media managers "+" card.
function InstanceStep({ setup, onSaved }: InstanceStepProps) {
  const [implementation, setImplementation] = useState<string>();

  const onSonarrPress = useCallback(() => setImplementation('Sonarr'), []);
  const onRadarrPress = useCallback(() => setImplementation('Radarr'), []);
  const skip = useApiAction();
  const { run } = skip;
  const onSkipPress = useCallback(() => {
    run(() => callRequestarrApi('setup/skip-instance', 'POST'), onSaved);
  }, [run, onSaved]);
  const isDone = setup.instance || setup.instanceSkipped;

  return (
    <SetupStep number={3} title="SetupInstance" isDone={isDone}>
      <div className={styles.intro}>{translate('SetupInstanceIntro')}</div>

      {setup.instanceSkipped && !setup.instance && (
        <div className={styles.intro}>{translate('SetupInstanceSkipped')}</div>
      )}

      {!isDone && !implementation && (
        <div className={styles.buttons}>
          <Button onPress={onSonarrPress}>
            {translate('InstanceAddSonarr')}
          </Button>
          <Button onPress={onRadarrPress}>
            {translate('InstanceAddRadarr')}
          </Button>
        </div>
      )}

      {!isDone && implementation && (
        <NewInstance
          key={implementation}
          implementation={implementation}
          onSaved={onSaved}
        />
      )}

      {!isDone && (
        <div className={styles.buttons}>
          <SpinnerButton isSpinning={skip.isBusy} onPress={onSkipPress}>
            {translate('SetupSkip')}
          </SpinnerButton>
        </div>
      )}

      {skip.problem && (
        <Alert kind={kinds.DANGER}>
          {translate('SetupSkipFailed', { problem: skip.problem })}
        </Alert>
      )}
    </SetupStep>
  );
}

export default InstanceStep;
