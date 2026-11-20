import React, { ReactNode } from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface ActionModalProps {
  isOpen: boolean;
  title: string;
  confirmLabel: string;

  // While what the choices start from is still loading.
  isConfirmDisabled?: boolean;
  children: ReactNode;
  onConfirm: () => void;
  onClose: () => void;
}

// A dialog with the choices an action needs (the seasons to add or remove, the access to give), then its button.
function ActionModal({
  isOpen,
  title,
  confirmLabel,
  isConfirmDisabled = false,
  children,
  onConfirm,
  onClose,
}: ActionModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{title}</ModalHeader>
        <ModalBody>{children}</ModalBody>
        <ModalFooter>
          <Button onPress={onClose}>{translate('Cancel')}</Button>
          <Button
            kind={kinds.PRIMARY}
            isDisabled={isConfirmDisabled}
            onPress={onConfirm}
          >
            {confirmLabel}
          </Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default ActionModal;
