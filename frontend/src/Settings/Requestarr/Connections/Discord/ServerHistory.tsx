import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import ProgressBar from 'Components/ProgressBar';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useHistoryPosting, { HistoryStatus } from '../../useHistoryPosting';
import styles from './ServerHistory.css';

// Cancel asked first: open while asked, closed again by either answer, or once posting finished with nothing left to
// cancel.
function useCancelConfirm(state: HistoryStatus['state'], cancel: () => void) {
  const [isConfirming, setIsConfirming] = useState(false);
  const onAsk = useCallback(() => setIsConfirming(true), []);
  const onKeep = useCallback(() => setIsConfirming(false), []);
  const onCancel = useCallback(() => {
    setIsConfirming(false);
    cancel();
  }, [cancel]);

  if (isConfirming && state === 'idle') {
    setIsConfirming(false);
  }

  return { isConfirming, onAsk, onKeep, onCancel };
}

// The whole server's imported history in Discord, like Sonarr's queue shows a download: Post History (once the switch above
// it is saved), then how far it got ("312 of 467 posted") with Pause, Resume and Cancel, which asks first, as the member
// page's Cancel History does.
function ServerHistory({ isDisabled }: { isDisabled: boolean }) {
  const { status, problem, act } = useHistoryPosting();
  const onPost = useCallback(() => act('post'), [act]);
  const onPause = useCallback(() => act('pause'), [act]);
  const onResume = useCallback(() => act('resume'), [act]);
  const cancelPosting = useCallback(() => act('cancel'), [act]);
  const state = status?.state ?? 'idle';
  const total = status?.total ?? 0;
  const posted = status?.posted ?? 0;
  const { isConfirming, onAsk, onKeep, onCancel } = useCancelConfirm(
    state,
    cancelPosting
  );

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
          <Button isDisabled={isDisabled} onPress={onPost}>
            {translate('HistoryPostServer')}
          </Button>
        )}
        {state === 'posting' && (
          <Button onPress={onPause}>{translate('HistoryPause')}</Button>
        )}
        {state === 'paused' && (
          <Button onPress={onResume}>{translate('HistoryResume')}</Button>
        )}
        {state !== 'idle' && (
          <Button kind={kinds.DANGER} onPress={onAsk}>
            {translate('HistoryCancel')}
          </Button>
        )}
      </div>
      {problem && <Alert kind={kinds.WARNING}>{problem}</Alert>}

      <ConfirmModal
        isOpen={isConfirming}
        kind={kinds.DANGER}
        title={translate('HistoryCancel')}
        message={translate('HistoryCancelMessage', { posted, total })}
        confirmLabel={translate('HistoryCancel')}
        onConfirm={onCancel}
        onCancel={onKeep}
      />
    </>
  );
}

export default ServerHistory;
