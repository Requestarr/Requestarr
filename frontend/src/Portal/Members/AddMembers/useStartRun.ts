import { useCallback, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import {
  OnboardAccess,
  OnboardJob,
  PortalServerMember,
} from '../../PortalTypes';
import { ActionOutcome } from '../../usePortalAction';

// Who is onboarded: their Discord ID, the name the run shows, and how many requests an import brought for them.
export type Who = Pick<
  PortalServerMember,
  'discordId' | 'name' | 'importedRequests'
>;

type Person = Pick<Who, 'discordId' | 'name'>;

// What everyone picked gets: their access, and whether their imported requests are posted in their channel.
export interface OnboardSetup {
  access: OnboardAccess[];
  postHistory: boolean;
}

// Before anything is chosen.
export const NO_SETUP: OnboardSetup = { access: [], postHistory: true };

// Starting a run for these people with this setup; its answer's ID, or why it couldn't start.
function useStartRun() {
  const [runId, setRunId] = useState<string>();
  const [refused, setRefused] = useState<ActionOutcome>();
  // Pressing twice never starts two runs for the same people.
  const [isStarting, setIsStarting] = useState(false);
  const start = useCallback((people: Person[], setup: OnboardSetup) => {
    setIsStarting(true);
    callApi<OnboardJob>('/portal/members/onboard', 'POST', {
      people: people.map(({ discordId, name }) => ({ discordId, name })),
      ...setup,
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
