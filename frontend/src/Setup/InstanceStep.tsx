import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import InstanceFields from 'Settings/Requestarr/InstanceFields';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
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

// The first Sonarr or Radarr: pick which, fill in its address and API key, Test, choose what requests use, Save.
// More can be added later under Settings, Sonarr and Radarr.
function InstanceStep({ setup, onSaved }: InstanceStepProps) {
  const [implementation, setImplementation] = useState<string>();

  const onSonarrPress = useCallback(() => setImplementation('Sonarr'), []);
  const onRadarrPress = useCallback(() => setImplementation('Radarr'), []);

  return (
    <SetupStep number={3} title="SetupInstance" isDone={setup.instance}>
      <div className={styles.intro}>{translate('SetupInstanceIntro')}</div>

      {!setup.instance && !implementation && (
        <div className={styles.buttons}>
          <Button onPress={onSonarrPress}>
            {translate('InstanceAddSonarr')}
          </Button>
          <Button onPress={onRadarrPress}>
            {translate('InstanceAddRadarr')}
          </Button>
        </div>
      )}

      {!setup.instance && implementation && (
        <NewInstance
          key={implementation}
          implementation={implementation}
          onSaved={onSaved}
        />
      )}
    </SetupStep>
  );
}

export default InstanceStep;
