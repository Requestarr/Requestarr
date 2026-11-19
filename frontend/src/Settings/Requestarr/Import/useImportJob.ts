import { useCallback, useEffect, useState } from 'react';
import { callRequestarrApi } from '../requestarrApi';
import { ImportJob } from './importTypes';

// How often the page asks how far reading has got.
const POLL_MILLISECONDS = 1500;

// An import from preview to confirm: start reads the channels (followed until the preview is ready), confirm writes it.
function useImportJob() {
  const [job, setJob] = useState<ImportJob>();
  const [error, setError] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);

  const call = useCallback((path: string, method = 'GET', body?: unknown) => {
    setIsBusy(true);
    setError(undefined);
    callRequestarrApi<ImportJob>(path, method, body)
      .then(setJob, (failure: Error) => setError(failure.message))
      .finally(() => setIsBusy(false));
  }, []);

  const start = useCallback(
    (channelIds: string[]) => call('import/preview', 'POST', { channelIds }),
    [call]
  );

  const confirm = useCallback(() => {
    if (job) {
      call(`import/preview/${job.id}/confirm`, 'POST');
    }
  }, [call, job]);

  useEffect(() => {
    if (job?.state !== 'reading') {
      return undefined;
    }

    const timer = setTimeout(
      () =>
        callRequestarrApi<ImportJob>(`import/preview/${job.id}`).then(
          setJob,
          (failure: Error) => {
            setError(failure.message);
            setJob(undefined);
          }
        ),
      POLL_MILLISECONDS
    );

    return () => clearTimeout(timer);
  }, [job]);

  return { job, error, isBusy, start, confirm };
}

export default useImportJob;
