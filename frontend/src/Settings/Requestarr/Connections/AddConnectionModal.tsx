import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import styles from './AddConnectionModal.css';

// What a section's "+" card can add, grouped by what it does, like Add Indexer. NotYet: what the group says while
// nothing in it is built.
export interface ChoiceGroup {
  legend: string;
  choices: { implementation: string; description: string }[];
  notYet?: string;
}

// Plex and Jellyfin arrive with media server links (Phase 3).
export const MEDIA_SERVERS: ChoiceGroup[] = [
  {
    legend: 'ConnectionsMediaServers',
    choices: [],
    notYet: 'ConnectionsMediaServersNotYet',
  },
];

interface ChoiceProps {
  implementation: string;
  description: string;
  onSelect: (implementation: string) => void;
}

function Choice({ implementation, description, onSelect }: ChoiceProps) {
  const onPress = useCallback(
    () => onSelect(implementation),
    [implementation, onSelect]
  );

  return (
    <Card className={styles.choice} onPress={onPress}>
      <div className={styles.name}>{implementation}</div>
      <div className={styles.description}>{translate(description)}</div>
    </Card>
  );
}

// IsLoading: the choices are still on their way (a provider's schema), as Prowlarr's Add Application shows.
interface AddConnectionModalProps {
  isOpen: boolean;
  isLoading?: boolean;
  groups: ChoiceGroup[];
  onSelect: (implementation: string) => void;
  onClose: () => void;
}

// The "+" card's choices, each a tile with its name and what it's for; picking one opens its edit modal, empty.
function AddConnectionModal({
  isOpen,
  isLoading = false,
  groups,
  onSelect,
  onClose,
}: AddConnectionModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{translate('AddConnection')}</ModalHeader>

        <ModalBody>
          {isLoading && <LoadingIndicator />}

          {!isLoading &&
            groups.map(({ legend, choices, notYet }) => (
              <FieldSet key={legend} legend={translate(legend)}>
                {notYet && !choices.length && (
                  <Alert kind={kinds.INFO}>{translate(notYet)}</Alert>
                )}

                <div className={styles.choices}>
                  {choices.map((choice) => (
                    <Choice
                      key={choice.implementation}
                      {...choice}
                      onSelect={onSelect}
                    />
                  ))}
                </div>
              </FieldSet>
            ))}
        </ModalBody>

        <ModalFooter>
          <Button onPress={onClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default AddConnectionModal;
