import React, { useCallback, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import { OnboardResult } from '../../PortalTypes';
import OnboardProgress from './OnboardProgress';
import SetUpPeople from './SetUpPeople';
import useStartRun, { NO_SETUP, OnboardSetup, Who } from './useStartRun';

interface OnboardModalContentProps {
  people: Who[];
  onClose: () => void;
}

// Set up, then watch it happen; Retry starts a new run with the same setup for those who failed.
function OnboardModalContent({ people, onClose }: OnboardModalContentProps) {
  const [setup, setSetup] = useState<OnboardSetup>(NO_SETUP);
  const { runId, refused, isStarting, start } = useStartRun();
  const onConfirm = useCallback(
    (chosen: OnboardSetup) => {
      setSetup(chosen);
      start(people, chosen);
    },
    [people, start]
  );
  const onRetry = useCallback(
    (failed: OnboardResult[]) => start(failed, setup),
    [setup, start]
  );

  return runId ? (
    <OnboardProgress
      key={runId}
      people={people}
      runId={runId}
      refused={refused}
      isStarting={isStarting}
      onRetry={onRetry}
      onClose={onClose}
    />
  ) : (
    <SetUpPeople
      people={people}
      refused={refused}
      isStarting={isStarting}
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );
}

interface OnboardModalProps {
  // The people picked who can be onboarded; none keeps it closed.
  people: Who[];
  onClose: () => void;
}

// Onboard in one window, from a member's page and the lists people are picked on: what the picks get (the server's
// defaults to start), then each one's progress, as Prowlarr's bulk Edit opens over its indexer list.
function OnboardModal({ people, onClose }: OnboardModalProps) {
  const isOpen = people.length > 0;

  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      {isOpen && <OnboardModalContent people={people} onClose={onClose} />}
    </Modal>
  );
}

export default OnboardModal;
