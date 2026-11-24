import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ProgressBar from 'Components/ProgressBar';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useHistoryPosting from '../../useHistoryPosting';
import styles from './ServerHistory.css';

// The whole server's imported history in Discord, like Sonarr's queue shows a download: Post History, then how far it
// got ("312 of 467 posted") with Pause, Resume and Cancel.
function ServerHistory() {
  const { status, problem, act } = useHistoryPosting();
  const onPost = useCallback(() => act('post'), [act]);
  const onPause = useCallback(() => act('pause'), [act]);
  const onResume = useCallback(() => act('resume'), [act]);
  const onCancel = useCallback(() => act('cancel'), [act]);
  const state = status?.state ?? 'idle';
  const total = status?.total ?? 0;
  const posted = status?.posted ?? 0;

  return (
    <>
      {total > 0 && (
        <div className={styles.progress}>
          <ProgressBar
            progress={(posted / total) * 100}
            kind={state === 'paused' ? kinds.WARNING : kinds.PRIMARY}
            size={sizes.LARGE}
            showText={true}
            text={translate('HistoryProgress', { posted, total })}
          />
        </div>
      )}

      <div className={styles.buttons}>
        {state === 'idle' && (
          <Button onPress={onPost}>{translate('HistoryPostServer')}</Button>
        )}
        {state === 'posting' && (
          <Button onPress={onPause}>{translate('HistoryPause')}</Button>
        )}
        {state === 'paused' && (
          <Button onPress={onResume}>{translate('HistoryResume')}</Button>
        )}
        {state !== 'idle' && (
          <Button kind={kinds.DANGER} onPress={onCancel}>
            {translate('HistoryCancel')}
          </Button>
        )}
      </div>
      {problem && <Alert kind={kinds.WARNING}>{problem}</Alert>}
    </>
  );
}

export default ServerHistory;
