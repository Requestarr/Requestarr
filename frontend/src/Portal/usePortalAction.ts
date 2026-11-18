import { useCallback, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';

export interface ActionOutcome {
  isDone: boolean;
  message: string;
}

interface Answer {
  message: string;
}

// An admin action on a portal page (/api/v1/portal/{path}): busy while it runs, then Requestarr's answer in the words
// Discord would use. onDone runs after one that was done, to show the page as it is now.
function usePortalAction(onDone: () => void) {
  const [isBusy, setIsBusy] = useState(false);
  const [outcome, setOutcome] = useState<ActionOutcome>();

  const act = useCallback(
    (path: string, body: unknown = {}, method = 'POST') => {
      setIsBusy(true);

      callApi<Answer>(`/portal/${path}`, method, body).then(
        (answer) => {
          setIsBusy(false);
          setOutcome({ isDone: true, message: answer.message });
          onDone();
        },
        (failure: Error) => {
          setIsBusy(false);
          setOutcome({ isDone: false, message: failure.message });
        }
      );
    },
    [onDone]
  );

  return { act, isBusy, outcome };
}

export type PortalAction = ReturnType<typeof usePortalAction>;

export default usePortalAction;
