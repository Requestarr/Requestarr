import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds } from 'Helpers/Props';
import MediaManagerForm, {
  useMediaManagerSettings,
} from 'Settings/Requestarr/Connections/MediaManagers/MediaManagerForm';
import { callRequestarrApi } from 'Settings/Requestarr/requestarrApi';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
import useApiAction from 'Settings/Requestarr/useApiAction';
import {
  fetchMediaManagerSchema,
  saveMediaManager,
  selectMediaManagerSchema,
  testMediaManager,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './SetupPage.css';

// The new instance's form, the Connections page's (MediaManagerForm), once the schema to start it from has loaded.
function NewInstanceForm({ onSaved }: { onSaved: () => void }) {
  const dispatch = useDispatch();
  const settings = useMediaManagerSettings();
  const { isSaving, isTesting, saveError } = settings;
  const wasSaving = usePrevious(isSaving);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onSaved();
    }
  }, [wasSaving, isSaving, saveError, onSaved]);

  const onTestPress = useCallback(
    () => dispatch(testMediaManager({})),
    [dispatch]
  );
  const onSavePress = useCallback(
    () => dispatch(saveMediaManager({})),
    [dispatch]
  );

  return (
    <>
      <MediaManagerForm settings={settings} isReadOnly={false} />

      <div className={styles.buttons}>
        <SpinnerButton isSpinning={isTesting} onPress={onTestPress}>
          {translate('Test')}
        </SpinnerButton>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isSaving}
          onPress={onSavePress}
        >
          {translate('Save')}
        </SpinnerButton>
      </div>
    </>
  );
}

interface NewInstanceProps {
  implementation: string;
  onSaved: () => void;
}

// A new Sonarr or Radarr, started from the media managers' schema as the Connections page's Add modal starts one.
function NewInstance({ implementation, onSaved }: NewInstanceProps) {
  const dispatch = useDispatch();
  const { isSchemaPopulated, selectedSchema } = useSelector(
    (state: AppState) => state.settings.mediaManagers
  );

  useEffect(() => {
    dispatch(fetchMediaManagerSchema());
  }, [dispatch]);

  useEffect(() => {
    if (isSchemaPopulated) {
      dispatch(selectMediaManagerSchema({ implementation }));
    }
  }, [dispatch, isSchemaPopulated, implementation]);

  if (selectedSchema.implementation !== implementation) {
    return <LoadingIndicator />;
  }

  return <NewInstanceForm onSaved={onSaved} />;
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
