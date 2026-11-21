import React, { useCallback, useEffect } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import usePrevious from 'Helpers/Hooks/usePrevious';
import { kinds, sizes } from 'Helpers/Props';
import { clearPendingChanges } from 'Store/Actions/baseActions';
import {
  cancelSaveMediaManager,
  cancelTestMediaManager,
  saveMediaManager,
  testMediaManager,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import ConnectionModalFooter from '../ConnectionModalFooter';
import MediaManagerForm, { useMediaManagerSettings } from './MediaManagerForm';

const SECTION = 'settings.mediaManagers';

interface EditMediaManagerModalContentProps {
  id?: number;
  isReadOnly: boolean;
  onModalClose: () => void;
}

// Prowlarr's EditApplicationModalContent with its connector, as hooks: the form, then Test, Cancel and Save; closed
// once a save goes through. No Delete: an instance is turned off instead, since its requests keep their history.
function EditMediaManagerModalContent({
  id,
  isReadOnly,
  onModalClose,
}: EditMediaManagerModalContentProps) {
  const dispatch = useDispatch();
  const settings = useMediaManagerSettings(id);
  const { isFetching, error, isSaving, saveError, isTesting, item } = settings;
  const wasSaving = usePrevious(isSaving);

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

  const title = id
    ? translate('InstanceEditTitle', { name: item.name.value })
    : translate('InstanceAddTitle', { type: item.implementationName });

  return (
    <ModalContent onModalClose={onModalClose}>
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
      </ModalBody>

      <ConnectionModalFooter
        isReadOnly={isReadOnly}
        isBusy={isSaving || isTesting}
        canSave={true}
        onTest={onTestPress}
        onCancel={onModalClose}
        onSave={onSavePress}
      />
    </ModalContent>
  );
}

interface EditMediaManagerModalProps {
  isOpen: boolean;
  id?: number;
  isReadOnly: boolean;
  onModalClose: () => void;
}

// Adds a Sonarr or Radarr, or changes a saved one, as Prowlarr's EditApplicationModal(Connector): closing it drops the
// changes not saved and stops a save or test still running.
function EditMediaManagerModal({
  isOpen,
  id,
  isReadOnly,
  onModalClose,
}: EditMediaManagerModalProps) {
  const dispatch = useDispatch();

  const onClose = useCallback(() => {
    dispatch(clearPendingChanges({ section: SECTION }));
    dispatch(cancelTestMediaManager({ section: SECTION }));
    dispatch(cancelSaveMediaManager({ section: SECTION }));
    onModalClose();
  }, [dispatch, onModalClose]);

  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onClose}>
      {isOpen && (
        <EditMediaManagerModalContent
          id={id}
          isReadOnly={isReadOnly}
          onModalClose={onClose}
        />
      )}
    </Modal>
  );
}

export default EditMediaManagerModal;
