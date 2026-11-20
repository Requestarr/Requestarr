import React, { ReactNode } from 'react';
import Alert from 'Components/Alert';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { SettingsSection } from '../RequestarrSettingsTypes';
import SettingsSections from '../SettingsSections';
import useSettingsForm from '../useSettingsForm';
import ConnectionModalFooter from './ConnectionModalFooter';

interface ContentProps {
  title: string;
  sections: SettingsSection[];
  showAdvanced: boolean;
  children?: ReactNode;
  onSaved: () => void;
  onClose: () => void;
}

function SettingsConnectionContent({
  title,
  sections,
  showAdvanced,
  children,
  onSaved,
  onClose,
}: ContentProps) {
  const form = useSettingsForm(onSaved);
  const { settings, saveError } = form;
  const isReadOnly = settings?.provisioned ?? true;

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{title}</ModalHeader>

      <ModalBody>
        {settings?.provisioned && (
          <Alert kind={kinds.INFO}>
            {translate('SettingsProvisionedNotice')}
          </Alert>
        )}

        {saveError && <Alert kind={kinds.DANGER}>{saveError}</Alert>}

        {children}

        {settings && (
          <SettingsSections
            sections={sections}
            form={form}
            showAdvanced={showAdvanced}
          />
        )}
      </ModalBody>

      <ConnectionModalFooter
        isReadOnly={isReadOnly}
        isBusy={form.isSaving}
        canSave={form.hasChanges}
        onCancel={onClose}
        onSave={form.onSave}
      />
    </ModalContent>
  );
}

interface SettingsConnectionModalProps extends ContentProps {
  isOpen: boolean;
}

// The edit modal of a connection kept in Requestarr's settings (the Discord bot, The Movie Database, GitHub): its
// fields start from what's saved each time it opens, and Save keeps only what changed.
function SettingsConnectionModal({
  isOpen,
  onClose,
  ...content
}: SettingsConnectionModalProps) {
  return (
    <Modal isOpen={isOpen} size={sizes.LARGE} onModalClose={onClose}>
      {isOpen && <SettingsConnectionContent {...content} onClose={onClose} />}
    </Modal>
  );
}

export default SettingsConnectionModal;
