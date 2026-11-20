import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import useDeletesFiles from '../useDeletesFiles';
import { actionLabel } from './ActionButton';
import styles from './BulkActionModal.css';

// What each request's call carries: a denial's reason; a removal of the whole title, forced or not.
function bodyFor(action: string, reason: string, force: boolean) {
  switch (action) {
    case 'deny':
      return { reason };
    case 'remove':
      return { seasons: [], force };
    default:
      return {};
  }
}

interface BulkActionContentProps {
  action: string;
  requests: PortalRequest[];
  onConfirm: (body: object) => void;
  onClose: () => void;
}

function BulkActionContent(props: BulkActionContentProps) {
  const { action, requests, onConfirm, onClose } = props;
  const deletesFiles = useDeletesFiles();
  const [reason, setReason] = useState('');
  const [force, setForce] = useState(false);
  const label = actionLabel(action);

  const onReasonChange = useCallback(
    ({ value }: { value: string }) => setReason(value),
    []
  );
  const onForceChange = useCallback(
    ({ value }: { value: boolean }) => setForce(value),
    []
  );
  const onConfirmPress = useCallback(
    () => onConfirm(bodyFor(action, reason, force)),
    [action, reason, force, onConfirm]
  );

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>
        {translate('PortalBulkTitle', {
          action: label,
          count: requests.length,
        })}
      </ModalHeader>
      <ModalBody>
        <ul className={styles.titles}>
          {requests.map((request) => (
            <li key={request.id}>{titleText(request)}</li>
          ))}
        </ul>
        {action === 'deny' && (
          <FormGroup>
            <FormLabel>{translate('PortalReason')}</FormLabel>
            <FormInputGroup
              type={inputTypes.TEXT}
              name="reason"
              value={reason}
              helpText={translate('PortalReasonHelpText')}
              onChange={onReasonChange}
            />
          </FormGroup>
        )}
        {action === 'remove' &&
          requests.some((r) => r.status === 'notInLibrary') && (
            <Alert kind={kinds.INFO}>{translate('PortalRecordOnlyNote')}</Alert>
          )}
        {action === 'remove' && deletesFiles === false && (
          <Alert kind={kinds.INFO}>{translate('PortalDeletingOffNote')}</Alert>
        )}
        {action === 'remove' && (
          <FormGroup>
            <FormLabel>{translate('PortalRemovalForce')}</FormLabel>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="force"
              value={force}
              helpText={translate('PortalBulkForceHelpText')}
              onChange={onForceChange}
            />
          </FormGroup>
        )}
      </ModalBody>
      <ModalFooter>
        <Button onPress={onClose}>{translate('Cancel')}</Button>
        <Button kind={kinds.DANGER} onPress={onConfirmPress}>
          {label}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

interface BulkActionModalProps {
  action?: string;
  requests: PortalRequest[];
  onConfirm: (body: object) => void;
  onClose: () => void;
}

// The selection bar's confirmation: the action, every title it acts on, and what it needs (a reason; for a removal, Force).
function BulkActionModal({
  action,
  requests,
  onConfirm,
  onClose,
}: BulkActionModalProps) {
  return (
    <Modal isOpen={!!action} onModalClose={onClose}>
      {action && (
        <BulkActionContent
          action={action}
          requests={requests}
          onConfirm={onConfirm}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

export default BulkActionModal;
