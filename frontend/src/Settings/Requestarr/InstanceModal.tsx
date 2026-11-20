import React from 'react';
import Alert from 'Components/Alert';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ConnectionModalFooter from './Connections/ConnectionModalFooter';
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
        {isReadOnly && (
          <Alert kind={kinds.INFO}>
            {translate('SettingsProvisionedNotice')}
          </Alert>
        )}

        <InstanceFields
          form={form}
          isReadOnly={isReadOnly}
          hasTestButton={false}
        />
      </ModalBody>

      <ConnectionModalFooter
        isReadOnly={isReadOnly}
        isBusy={form.isBusy}
        canSave={true}
        onTest={form.onTest}
        onCancel={onClose}
        onSave={form.onSave}
      />
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
