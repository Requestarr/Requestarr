import React, { useCallback, useState } from 'react';
import { useLocation } from 'react-router-dom';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../../Components/ActionOutcomeAlert';
import PortalPage from '../../Components/PortalPage';
import {
  OnboardAccess,
  OnboardJob,
  OnboardResult,
  PortalServerMember,
} from '../../PortalTypes';
import usePortal from '../../usePortal';
import { ActionOutcome } from '../../usePortalAction';
import OnboardProgress from './OnboardProgress';
import PickPeople from './PickPeople';
import SetUpPeople from './SetUpPeople';
import styles from '../../Portal.css';

type Who = Pick<PortalServerMember, 'discordId' | 'name'>;

// Starting a run for these people with this access; its answer's ID, or why it couldn't start.
function useStartRun() {
  const [runId, setRunId] = useState<string>();
  const [refused, setRefused] = useState<ActionOutcome>();
  const start = useCallback((people: Who[], access: OnboardAccess[]) => {
    callApi<OnboardJob>('/portal/members/onboard', 'POST', {
      people: people.map(({ discordId, name }) => ({ discordId, name })),
      access,
    }).then(
      (run) => setRunId(run.id),
      (failure: Error) =>
        setRefused({ isDone: false, message: failure.message })
    );
  }, []);

  return { runId, refused, start };
}

interface StepsProps {
  people: PortalServerMember[];
  pick: string | null;
}

// Pick, set up, then watch it happen; Retry starts a new run with the same setup for those who failed.
function Steps({ people, pick }: StepsProps) {
  const [picked, setPicked] = useState<PortalServerMember[]>();
  const [access, setAccess] = useState<OnboardAccess[]>([]);
  const { runId, refused, start } = useStartRun();
  const onBack = useCallback(() => setPicked(undefined), []);
  const onConfirm = useCallback(
    (chosen: OnboardAccess[]) => {
      setAccess(chosen);
      start(picked ?? [], chosen);
    },
    [picked, start]
  );
  const onRetry = useCallback(
    (failed: OnboardResult[]) => start(failed, access),
    [access, start]
  );

  if (runId) {
    return <OnboardProgress key={runId} runId={runId} onRetry={onRetry} />;
  }

  if (picked) {
    return (
      <>
        <ActionOutcomeAlert outcome={refused} />
        <SetUpPeople people={picked} onBack={onBack} onConfirm={onConfirm} />
      </>
    );
  }

  return people.length ? (
    <PickPeople people={people} pick={pick} onNext={setPicked} />
  ) : (
    <div className={styles.empty}>{translate('PortalNobodyToAdd')}</div>
  );
}

// Add member: everyone in the Discord server who isn't onboarded, to pick one or many, set up together and onboard,
// paced for Discord, with each person's progress.
function AddMembersPage() {
  const result = usePortal<PortalServerMember[]>('server-members');
  const pick = new URLSearchParams(useLocation().search).get('pick');
  const renderSteps = useCallback(
    (people: PortalServerMember[]) => <Steps people={people} pick={pick} />,
    [pick]
  );

  return (
    <PortalPage title={translate('PortalActionAddMember')} result={result}>
      {renderSteps}
    </PortalPage>
  );
}

export default AddMembersPage;
