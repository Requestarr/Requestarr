import { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import showOutcome, { ActionOutcome } from './showOutcome';

export type { ActionOutcome };

interface Answer {
  message: string;
}

// An admin action on a portal page (/api/v1/portal/{path}): busy while it runs, then Requestarr's answer in the words
// Discord would use, in the sidebar's messages (and as outcome, for a dialog that shows it too). onDone runs after each, to show the page as it is now: a refused action can still have changed
// something (an approval Sonarr refused leaves the request failed). It's told whether the action was done.
function usePortalAction(onDone: (isDone: boolean) => void) {
  const [isBusy, setIsBusy] = useState(false);
  const [outcome, setOutcome] = useState<ActionOutcome>();
  const dispatch = useDispatch();
  const settle = useCallback(
    (result: ActionOutcome) => {
      setIsBusy(false);
      setOutcome(result);
      showOutcome(dispatch, 'portalAction', result);
      onDone(result.isDone);
    },
    [dispatch, onDone]
  );

  const act = useCallback(
    (path: string, body: unknown = {}, method = 'POST') => {
      setIsBusy(true);

      callApi<Answer>(`/portal/${path}`, method, body).then(
        (answer) => settle({ isDone: true, message: answer.message }),
        (failure: Error) => settle({ isDone: false, message: failure.message })
      );
    },
    [settle]
  );

  return { act, isBusy, outcome };
}

export type PortalAction = ReturnType<typeof usePortalAction>;

export default usePortalAction;
