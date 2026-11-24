import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { plainText } from '../../portalText';
import { OnboardResult } from '../../PortalTypes';
import OnboardProgress from './OnboardProgress';
import SetUpPeople from './SetUpPeople';
import useStartRun, { NO_SETUP, OnboardSetup, Who } from './useStartRun';

interface OnboardModalContentProps {
  people: Who[];
  onClose: () => void;
}

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

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{translate('PortalActionOnboard')}</ModalHeader>
      <ModalBody>
        {refused && (
          <Alert kind={kinds.DANGER}>{plainText(refused.message)}</Alert>
        )}
        {runId ? (
          <OnboardProgress
            key={runId}
            runId={runId}
            isStarting={isStarting}
            onRetry={onRetry}
          />
        ) : (
          <SetUpPeople
            people={people}
            isStarting={isStarting}
            backLabel={translate('Cancel')}
            onBack={onClose}
            onConfirm={onConfirm}
          />
        )}
      </ModalBody>
      {runId && (
        <ModalFooter>
          <Button onPress={onClose}>{translate('Close')}</Button>
        </ModalFooter>
      )}
    </ModalContent>
  );
}

interface OnboardModalProps {
  // The people picked on the Members list who can be onboarded; none keeps it closed.
  people: Who[];
  onClose: () => void;
}

// Onboard from the Members list in one window: what the picks get (the server's defaults to start), Confirm, then each
// one's progress, without leaving the list, as Prowlarr's bulk Edit opens over its indexer list.
function OnboardModal({ people, onClose }: OnboardModalProps) {
  const isOpen = people.length > 0;

  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      {isOpen && <OnboardModalContent people={people} onClose={onClose} />}
    </Modal>
  );
}

export default OnboardModal;
