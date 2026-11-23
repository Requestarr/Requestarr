import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import MediaManagersSection from 'Settings/Requestarr/Connections/MediaManagers/MediaManagersSection';
import { callRequestarrApi } from 'Settings/Requestarr/requestarrApi';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
import useApiAction from 'Settings/Requestarr/useApiAction';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './SetupPage.css';

interface InstanceStepProps {
  setup: SetupStatus;
  onSaved: () => void;
}

// The first Sonarr or Radarr, exactly as Settings, then Connections adds them: the Media managers cards, the "+" card
// with its grouped Add modal, and the edit modal (the form appears only there). The step is done once one is saved,
// which the page notices on its own; Skip for now leaves it for later.
function InstanceStep({ setup, onSaved }: InstanceStepProps) {
  const skip = useApiAction();
  const { run } = skip;
  const onSkipPress = useCallback(() => {
    run(() => callRequestarrApi('setup/skip-instance', 'POST'), onSaved);
  }, [run, onSaved]);
  const isDone = setup.instance || setup.instanceSkipped;
  const settings = { provisioned: setup.provisioned, values: {} };

  return (
    <SetupStep number={3} title="SetupInstance" isDone={isDone}>
      <div className={styles.intro}>{translate('SetupInstanceIntro')}</div>

      {setup.instanceSkipped && !setup.instance && (
        <div className={styles.intro}>{translate('SetupInstanceSkipped')}</div>
      )}

      <MediaManagersSection settings={settings} />

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
