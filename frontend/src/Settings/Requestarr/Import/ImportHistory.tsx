import React, { useCallback, useEffect } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import ProgressBar from 'Components/ProgressBar';
import { inputTypes, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { callRequestarrApi } from '../requestarrApi';
import { AppSettings } from '../RequestarrSettingsTypes';
import useApiAction from '../useApiAction';
import useRequestarrApi from '../useRequestarrApi';
import styles from './ImportPage.css';

interface HistoryStatus {
  state: 'idle' | 'posting' | 'paused';
  total: number;
  posted: number;
}

// How often the page reads the progress again.
const PROGRESS_SECONDS = 3;
const SWITCHES = ['importPostChannels', 'importPostFeed'];

// The two switches, saved as they're flipped: whether an import posts its history in members' channels and in the
// admin feed. Both start on.
function HistorySwitches() {
  const { data, setData } = useRequestarrApi<AppSettings>('settings');
  const save = useApiAction();
  const { run } = save;
  const onChange = useCallback(
    ({ name, value }: { name: string; value: boolean }) =>
      run(
        () =>
          callRequestarrApi<AppSettings>('settings', 'PUT', { [name]: value }),
        setData
      ),
    [run, setData]
  );

  return (
    <>
      {SWITCHES.map((name) => (
        <FormGroup key={name}>
          <FormLabel>{translate(`ImportHistory_${name}`)}</FormLabel>
          <FormInputGroup
            type={inputTypes.CHECK}
            name={name}
            value={data?.values[name] === true}
            helpText={translate(`ImportHistory_${name}Help`)}
            onChange={onChange}
          />
        </FormGroup>
      ))}
      {save.problem && <Alert kind={kinds.DANGER}>{save.problem}</Alert>}
    </>
  );
}

// Reads the progress again every few seconds while the page is open: an import started here, or a member onboarded
// meanwhile, starts posting without the page being told.
function useHistoryStatus() {
  const status = useRequestarrApi<HistoryStatus>('import/history');
  const { reload } = status;

  useEffect(() => {
    const timer = setInterval(reload, PROGRESS_SECONDS * 1000);
    return () => clearInterval(timer);
  }, [reload]);

  return status;
}

// Imported history in Discord, like Sonarr's queue shows a download: how far it got ("312 of 467 posted") with
// Pause, Resume and Cancel, and "Post history now" for an import run before posting existed.
function ImportHistory() {
  const { data: status, setData } = useHistoryStatus();
  const action = useApiAction();
  const { run } = action;
  const onAction = useCallback(
    (path: string) =>
      run(
        () =>
          callRequestarrApi<HistoryStatus>(`import/history/${path}`, 'POST'),
        setData
      ),
    [run, setData]
  );
  const onPause = useCallback(() => onAction('pause'), [onAction]);
  const onResume = useCallback(() => onAction('resume'), [onAction]);
  const onCancel = useCallback(() => onAction('cancel'), [onAction]);
  const onPost = useCallback(() => onAction('post'), [onAction]);
  const state = status?.state ?? 'idle';
  const total = status?.total ?? 0;
  const posted = status?.posted ?? 0;

  return (
    <FieldSet legend={translate('ImportHistory')}>
      <HistorySwitches />

      {total > 0 && (
        <div className={styles.history}>
          <ProgressBar
            progress={(posted / total) * 100}
            kind={state === 'paused' ? kinds.WARNING : kinds.PRIMARY}
            size={sizes.LARGE}
            showText={true}
            text={translate('ImportHistoryProgress', { posted, total })}
          />
        </div>
      )}

      <div className={styles.historyButtons}>
        {state === 'posting' && (
          <Button onPress={onPause}>{translate('ImportHistoryPause')}</Button>
        )}
        {state === 'paused' && (
          <Button onPress={onResume}>{translate('ImportHistoryResume')}</Button>
        )}
        {state !== 'idle' && (
          <Button kind={kinds.DANGER} onPress={onCancel}>
            {translate('ImportHistoryCancel')}
          </Button>
        )}
        {state === 'idle' && (
          <Button onPress={onPost}>{translate('ImportHistoryPostNow')}</Button>
        )}
      </div>
      {action.problem && <Alert kind={kinds.DANGER}>{action.problem}</Alert>}
    </FieldSet>
  );
}

export default ImportHistory;
