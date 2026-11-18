import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import SearchField from '../Components/SearchField';
import useSearch from '../Components/useSearch';
import { PortalServerMember } from '../PortalTypes';
import usePortalAction, { PortalAction } from '../usePortalAction';
import styles from '../Portal.css';

interface CandidateProps {
  person: PortalServerMember;
  action: PortalAction;
}

function Candidate({ person, action }: CandidateProps) {
  const onAddPress = useCallback(
    () => action.act('members', { discordId: person.discordId }),
    [action, person]
  );

  return (
    <div className={styles.found}>
      <span>
        {person.name} <span className={styles.muted}>@{person.username}</span>
      </span>
      <Button kind={kinds.PRIMARY} onPress={onAddPress}>
        {translate('PortalActionAddMember')}
      </Button>
    </div>
  );
}

interface AddMemberModalProps {
  isOpen: boolean;
  onClose: () => void;
  onDone: () => void;
}

// Add member, as the admin panel's member picker: someone in the Discord server who isn't a member yet gets their
// channel and the default access.
function AddMemberModal({ isOpen, onClose, onDone }: AddMemberModalProps) {
  const action = usePortalAction(onDone);
  const search = useSearch<PortalServerMember>('server-members');

  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{translate('PortalActionAddMember')}</ModalHeader>
        <ModalBody>
          <SearchField
            {...search}
            helpText={translate('PortalAddMemberHelpText')}
          />
          {search.problem && (
            <Alert kind={kinds.DANGER}>{search.problem}</Alert>
          )}
          {search.found?.length === 0 && (
            <div className={styles.empty}>
              {translate('PortalNothingFound')}
            </div>
          )}
          {search.found?.map((person) => (
            <Candidate key={person.discordId} person={person} action={action} />
          ))}
          <ActionOutcomeAlert outcome={action.outcome} />
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}

export default AddMemberModal;
