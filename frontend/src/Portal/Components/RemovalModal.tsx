import React from 'react';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import RemovalStart from './RemovalStart';

interface RemovalModalProps {
  isOpen: boolean;
  onStart: (seasons: number[], force: boolean) => void;
  onClose: () => void;
}

// Start removal on a request's title: what it covers, then Start removal (Force asks once more). Each opening starts
// empty.
function RemovalModal({ isOpen, onStart, onClose }: RemovalModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{translate('PortalActionRemove')}</ModalHeader>
        <ModalBody>{isOpen && <RemovalStart onStart={onStart} />}</ModalBody>
      </ModalContent>
    </Modal>
  );
}

export default RemovalModal;
