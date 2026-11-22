import { useCallback, useEffect, useRef, useState } from 'react';
import { callRequestarrApi } from '../requestarrApi';
import { ImportJob } from './importTypes';

// How often the page asks how far reading, or the import's command in System, then Tasks, has got.
const POLL_MILLISECONDS = 1500;

// Asks after a job while it's reading or importing; answers reach the page through the callbacks.
function useFollowJob(
  job: ImportJob | undefined,
  onAnswer: (answer: ImportJob) => void,
  onFailure: (failure: Error) => void
) {
  useEffect(() => {
    if (job?.state !== 'reading' && job?.state !== 'importing') {
      return undefined;
    }

    const timer = setTimeout(
      () =>
        callRequestarrApi<ImportJob>(`import/preview/${job.id}`).then(
          onAnswer,
          onFailure
        ),
      POLL_MILLISECONDS
    );

    return () => clearTimeout(timer);
  }, [job, onAnswer, onFailure]);
}

// An import from preview to confirm: start reads the channels (followed until the preview is ready), confirm writes it,
// reset forgets the preview when no channel is picked. Only answers to the latest call are kept: picking channels
// quickly starts several previews, and an earlier one answering late mustn't replace the newest.
function useImportJob() {
  const [job, setJob] = useState<ImportJob>();
  const [error, setError] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);
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
      .finally(() => isLatest() && setIsBusy(false));
  }, []);

  const start = useCallback(
    (channelIds: string[]) => call('import/preview', 'POST', { channelIds }),
    [call]
  );
  const confirm = useCallback(
    () => job && call(`import/preview/${job.id}/confirm`, 'POST'),
    [call, job]
  );
  const reset = useCallback(() => {
    latest.current++;
    setJob(undefined);
    setError(undefined);
    setIsBusy(false);
  }, []);

  // A job's answer counts only while it's still the page's job.
  const onAnswer = useCallback(
    (answer: ImportJob) =>
      setJob((current) => (current?.id === answer.id ? answer : current)),
    []
  );
  const onFailure = useCallback((failure: Error) => {
    setError(failure.message);
    setJob(undefined);
  }, []);

  useFollowJob(job, onAnswer, onFailure);

  return { job, error, isBusy, start, confirm, reset };
}

export default useImportJob;
