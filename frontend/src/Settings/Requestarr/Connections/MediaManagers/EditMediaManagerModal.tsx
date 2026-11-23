import React, { useCallback, useEffect, useRef, useState } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds, sizes } from 'Helpers/Props';
import PendingChangesModal from 'Settings/PendingChangesModal';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  cancelSaveMediaManager,
  cancelTestMediaManager,
  saveMediaManager,
  testMediaManager,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import ConnectionModalFooter from '../ConnectionModalFooter';
import useLeaveWarning from '../useLeaveWarning';
import DeleteMediaManagerModal from './DeleteMediaManagerModal';
import MediaManagerForm, {
  MEDIA_MANAGERS_SECTION,
  useMediaManagerSettings,
} from './MediaManagerForm';

interface FailedRequest {
  responseJSON?: { errorMessage?: string }[] | { message?: string };
}

// Why the last test or save failed, in the server's words.
function failureOf(error?: object | null) {
  const answer = (error as FailedRequest | undefined)?.responseJSON;

  return Array.isArray(answer)
    ? answer.map((failure) => failure.errorMessage).join(' ')
    : answer?.message;
}

// Why the last Test or Save failed, just above the footer and scrolled to, so it shows where the admin is looking
// instead of only at the top of a long form.
function Failure({ error }: { error?: object | null }) {
  const ref = useRef<HTMLDivElement>(null);
  const failure = failureOf(error);

  useEffect(() => {
    ref.current?.scrollIntoView({ block: 'end' });
  }, [failure]);

  return failure ? (
    <div ref={ref}>
      <Alert kind={kinds.DANGER}>{failure}</Alert>
    </div>
  ) : null;
}

// OnModalClose closes it (after a save); onCancel asks first when there are changes not saved.
interface EditMediaManagerModalContentProps {
  id?: number;
  isReadOnly: boolean;
  onModalClose: () => void;
  onCancel: () => void;
  onDeleted: (problem?: string) => void;
}

// Prowlarr's EditApplicationModalContent with its connector, as hooks: the form, then Delete, Show Advanced, Test,
// Cancel and Save; closed once a save goes through.
function EditMediaManagerModalContent(
  props: EditMediaManagerModalContentProps
) {
  const { id, isReadOnly, onModalClose, onCancel, onDeleted } = props;
  const dispatch = useDispatch();
  const settings = useMediaManagerSettings(id);
  const { isFetching, error, isSaving, saveError, isTesting, item } = settings;
  const wasSaving = usePrevious(isSaving);
  const [isDeleting, setIsDeleting] = useState(false);

  useEffect(() => {
    if (wasSaving && !isSaving && !saveError) {
      onModalClose();
    }
  }, [wasSaving, isSaving, saveError, onModalClose]);

  const onSavePress = useCallback(
    () => dispatch(saveMediaManager({ id })),
    [dispatch, id]
  );
  const onTestPress = useCallback(
    () => dispatch(testMediaManager({ id })),
    [dispatch, id]
  );
  const onDeletePress = useCallback(() => setIsDeleting(true), []);
  const onDeleteCancel = useCallback(() => setIsDeleting(false), []);

  const title = id
    ? translate('InstanceEditTitle', { name: item.name.value })
    : translate('InstanceAddTitle', { type: item.implementationName });

  return (
    <ModalContent onModalClose={onCancel}>
      <ModalHeader>{title}</ModalHeader>

      <ModalBody>
        {isReadOnly && (
          <Alert kind={kinds.INFO}>
            {translate('SettingsProvisionedNotice')}
          </Alert>
        )}

        {isFetching && <LoadingIndicator />}

        {!isFetching && !!error && (
          <Alert kind={kinds.DANGER}>{translate('ConnectionLoadError')}</Alert>
        )}

        {!isFetching && !error && (
          <MediaManagerForm settings={settings} isReadOnly={isReadOnly} />
        )}

        <Failure error={saveError} />
      </ModalBody>

      <ConnectionModalFooter
        isReadOnly={isReadOnly}
        isSaving={isSaving}
        isTesting={isTesting}
        testError={saveError}
        canSave={true}
        hasAdvanced={true}
        onDelete={id ? onDeletePress : undefined}
        onTest={onTestPress}
        onCancel={onCancel}
        onSave={onSavePress}
      />

      {id && (
        <DeleteMediaManagerModal
          id={id}
          isOpen={isDeleting}
          onDeleted={onDeleted}
          onCancel={onDeleteCancel}
        />
      )}
    </ModalContent>
  );
}

interface EditMediaManagerModalProps {
  isOpen: boolean;
  id?: number;
  isReadOnly: boolean;
  onModalClose: () => void;
  onDeleted?: (problem?: string) => void;
}

// Adds a Sonarr or Radarr, or changes a saved one, as Prowlarr's EditApplicationModal(Connector): closing it drops the
// changes not saved and stops a save or test still running. Leaving with changes not saved (Cancel, the close button,
// Escape or a click outside) asks first.
function EditMediaManagerModal({
  isOpen,
  id,
  isReadOnly,
  onModalClose,
  onDeleted,
}: EditMediaManagerModalProps) {
  const dispatch = useDispatch();
  const { hasPendingChanges, item } = useMediaManagerSettings(id);

  const onClose = useCallback(() => {
    dispatch(clearPendingChanges({ section: MEDIA_MANAGERS_SECTION }));
    dispatch(cancelTestMediaManager({ section: MEDIA_MANAGERS_SECTION }));
    dispatch(cancelSaveMediaManager({ section: MEDIA_MANAGERS_SECTION }));
    onModalClose();
  }, [dispatch, onModalClose]);
  const leave = useLeaveWarning(isOpen && hasPendingChanges, onClose);

  const onDeletedClose = useCallback(
    (problem?: string) => {
      onClose();
      onDeleted?.(problem);
    },
    [onClose, onDeleted]
  );

  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={leave.onClose}>
      {isOpen && (
        <EditMediaManagerModalContent
          id={id}
          isReadOnly={isReadOnly}
          onModalClose={onClose}
          onCancel={leave.onClose}
          onDeleted={onDeletedClose}
        />
      )}

      <PendingChangesModal
        isOpen={leave.isAsking}
        message={translate('InstanceUnsavedChanges', {
          // Nothing picked yet (the modal is closed): no type to name.
          app: item.implementation?.value ?? '',
        })}
        onConfirm={leave.onConfirm}
        onCancel={leave.onCancel}
      />
    </Modal>
  );
}

export default EditMediaManagerModal;
