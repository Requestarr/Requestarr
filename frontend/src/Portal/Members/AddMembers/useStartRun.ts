import { useCallback, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import {
  OnboardAccess,
  OnboardJob,
  PortalServerMember,
} from '../../PortalTypes';
import { ActionOutcome } from '../../usePortalAction';

// Who is onboarded: their Discord ID and the name the run shows.
export type Who = Pick<PortalServerMember, 'discordId' | 'name'>;

// Starting a run for these people with this access; its answer's ID, or why it couldn't start.
function useStartRun() {
  const [runId, setRunId] = useState<string>();
  const [refused, setRefused] = useState<ActionOutcome>();
  // Pressing twice never starts two runs for the same people.
  const [isStarting, setIsStarting] = useState(false);
  const start = useCallback((people: Who[], access: OnboardAccess[]) => {
    setIsStarting(true);
    callApi<OnboardJob>('/portal/members/onboard', 'POST', {
      people: people.map(({ discordId, name }) => ({ discordId, name })),
      access,
    }).then(
      (run) => {
        setIsStarting(false);
        setRunId(run.id);
      },
      (failure: Error) => {
        setIsStarting(false);
        setRefused({ isDone: false, message: failure.message });
      }
    );
  }, []);

  return { runId, refused, isStarting, start };
}

export default useStartRun;
