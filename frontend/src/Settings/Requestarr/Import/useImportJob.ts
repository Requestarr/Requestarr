import { useCallback, useEffect, useRef, useState } from 'react';
import { callRequestarrApi } from '../requestarrApi';
import { ImportJob } from './importTypes';

// How often the page asks how far reading, or the import's command in System, then Tasks, has got.
const POLL_MILLISECONDS = 1500;

// Asks after a job while it's reading or importing. An answer, or a failure, that arrives after the page has moved on
// to another job (or none) is dropped, so it never replaces the newer one.
function useFollowJob(
  job: ImportJob | undefined,
  setJob: (job?: ImportJob) => void,
  setError: (error?: string) => void
) {
  useEffect(() => {
    if (job?.state !== 'reading' && job?.state !== 'importing') {
      return undefined;
    }

    let isCurrent = true;
    const timer = setTimeout(
      () =>
        callRequestarrApi<ImportJob>(`import/preview/${job.id}`).then(
          (answer) => isCurrent && setJob(answer),
          (failure: Error) => {
            if (isCurrent) {
              setError(failure.message);
              setJob(undefined);
            }
          }
        ),
      POLL_MILLISECONDS
    );

    return () => {
      isCurrent = false;
      clearTimeout(timer);
    };
  }, [job, setJob, setError]);
}

// An import from preview to confirm: start reads the channels (followed until the preview is ready), confirm writes it,
// reset forgets the preview when no channel is picked. Only answers to the latest call are kept: picking channels
// quickly starts several previews, and an earlier one answering late mustn't replace the newest.
function useImportJob() {
  const [job, setJob] = useState<ImportJob>();
  const [error, setError] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);
  const [isConfirming, setIsConfirming] = useState(false);
  const latest = useRef(0);

  const call = useCallback((path: string, method = 'GET', body?: unknown) => {
    const thisCall = ++latest.current;
    const isLatest = () => thisCall === latest.current;

    setIsBusy(true);
    setError(undefined);
    callRequestarrApi<ImportJob>(path, method, body)
      .then(
        (answer) => isLatest() && setJob(answer),
        (failure: Error) => isLatest() && setError(failure.message)
      )
      .finally(() => {
        if (isLatest()) {
          setIsBusy(false);
          setIsConfirming(false);
        }
      });
  }, []);

  const start = useCallback(
    (channelIds: string[]) => call('import/preview', 'POST', { channelIds }),
    [call]
  );
  const confirm = useCallback(() => {
    if (job) {
      setIsConfirming(true);
      call(`import/preview/${job.id}/confirm`, 'POST');
    }
  }, [call, job]);
  const reset = useCallback(() => {
    latest.current++;
    setJob(undefined);
    setError(undefined);
    setIsBusy(false);
  }, []);

  useFollowJob(job, setJob, setError);

  return { job, error, isBusy, isConfirming, start, confirm, reset };
}

export default useImportJob;
