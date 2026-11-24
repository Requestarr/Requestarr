import { useCallback, useEffect } from 'react';
import { callRequestarrApi } from './requestarrApi';
import useApiAction from './useApiAction';
import useRequestarrApi from './useRequestarrApi';

export interface HistoryStatus {
  state: 'idle' | 'posting' | 'paused';
  total: number;
  posted: number;
}

// How often the progress is read again.
const PROGRESS_SECONDS = 3;

// Imported history being posted in Discord: how far it got, read again every few seconds (a member onboarded meanwhile,
// or someone's /history, starts it without the page being told), and what can be done to it.
function useHistoryPosting() {
  const {
    data: status,
    reload,
    setData,
  } = useRequestarrApi<HistoryStatus>('import/history');
  const { run, problem } = useApiAction();
  const act = useCallback(
    (name: string) =>
      run(
        () =>
          callRequestarrApi<HistoryStatus>(`import/history/${name}`, 'POST'),
        setData
      ),
    [run, setData]
  );

  useEffect(() => {
    const timer = setInterval(reload, PROGRESS_SECONDS * 1000);
    return () => clearInterval(timer);
  }, [reload]);

  return { status, problem, act };
}

export default useHistoryPosting;
