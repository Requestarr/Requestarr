import React, { useCallback, useEffect, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import { OnboardResult } from '../../PortalTypes';
import OnboardProgress from './OnboardProgress';
import SetUpPeople from './SetUpPeople';
import useStartRun, { NO_SETUP, OnboardSetup, Who } from './useStartRun';

interface OnboardModalContentProps {
  people: Who[];
  onRun: () => void;
  onClose: () => void;
}

// Set up, then watch it happen; Retry starts a new run with the same setup for those who failed.
function OnboardModalContent({
  people,
  onRun,
  onClose,
}: OnboardModalContentProps) {
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

  useEffect(() => {
    if (runId) {
      onRun();
    }
  }, [runId, onRun]);

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

  // hasRun: onboarding started, rather than cancelled before it did.
  onClose: (hasRun: boolean) => void;
}

// Onboard in one window, from a member's page and the lists people are picked on: what the picks get (the server's
// defaults to start), then each one's progress, as Prowlarr's bulk Edit opens over its indexer list.
function OnboardModal({ people, onClose }: OnboardModalProps) {
  const isOpen = people.length > 0;
  const [hasRun, setHasRun] = useState(false);
  const onRun = useCallback(() => setHasRun(true), []);
  const onModalClose = useCallback(() => {
    setHasRun(false);
    onClose(hasRun);
  }, [hasRun, onClose]);

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      {isOpen && (
        <OnboardModalContent
          people={people}
          onRun={onRun}
          onClose={onModalClose}
        />
      )}
    </Modal>
  );
}

export default OnboardModal;
