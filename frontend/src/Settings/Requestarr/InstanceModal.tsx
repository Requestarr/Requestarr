import React from 'react';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InstanceFields from './InstanceFields';
import { Instance } from './RequestarrSettingsTypes';
import useInstanceForm from './useInstanceForm';

// What the modal edits: a saved instance, or a new one of a type.
export interface InstanceEdit {
  instance?: Instance;
  implementation: string;
}

interface InstanceModalContentProps extends InstanceEdit {
  isReadOnly: boolean;
  onSaved: () => void;
  onClose: () => void;
}

function InstanceModalContent({
  instance,
  implementation,
  isReadOnly,
  onSaved,
  onClose,
}: InstanceModalContentProps) {
  const form = useInstanceForm(instance, implementation, onSaved);
  const title = instance
    ? translate('InstanceEditTitle', { name: instance.settings.name })
    : translate('InstanceAddTitle', { type: implementation });

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{title}</ModalHeader>

      <ModalBody>
        <InstanceFields form={form} isReadOnly={isReadOnly} />
      </ModalBody>

      <ModalFooter>
        <Button onPress={onClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={form.isBusy}
          isDisabled={isReadOnly}
          onPress={form.onSave}
        >
          {translate('Save')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

interface InstanceModalProps {
  edit?: InstanceEdit;
  isReadOnly: boolean;
  onSaved: () => void;
  onClose: () => void;
}

// Adds a Sonarr or Radarr, or changes a saved one; closed while there's nothing to edit.
function InstanceModal({
  edit,
  isReadOnly,
  onSaved,
  onClose,
}: InstanceModalProps) {
  return (
    <Modal isOpen={!!edit} size={sizes.LARGE} onModalClose={onClose}>
      {edit && (
        <InstanceModalContent
          {...edit}
          isReadOnly={isReadOnly}
          onSaved={onSaved}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

export default InstanceModal;
