import React, { useCallback } from 'react';
import Card from 'Components/Card';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import styles from './AddConnectionModal.css';

// What can be added, grouped by what it does, like Add Indexer. Lidarr and the rest join Media managers as their
// content types arrive, and Media servers becomes a group with Plex and Jellyfin.
const GROUPS = [
  {
    legend: 'ConnectionsMediaManagers',
    choices: [
      { implementation: 'Sonarr', description: 'ConnectionSonarrDescription' },
      { implementation: 'Radarr', description: 'ConnectionRadarrDescription' },
    ],
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

interface AddConnectionModalProps {
  isOpen: boolean;
  onSelect: (implementation: string) => void;
  onClose: () => void;
}

// The "+" card's choices, each a tile with its name and what it's for; picking one opens its edit modal, empty.
function AddConnectionModal({
  isOpen,
  onSelect,
  onClose,
}: AddConnectionModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{translate('AddConnection')}</ModalHeader>

        <ModalBody>
          {GROUPS.map(({ legend, choices }) => (
            <FieldSet key={legend} legend={translate(legend)}>
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
