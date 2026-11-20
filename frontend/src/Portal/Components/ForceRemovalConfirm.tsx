import React from 'react';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useDeletesFiles, { forceRemovalMessage } from '../useDeletesFiles';

interface ForceRemovalConfirmProps {
  onConfirm: () => void;
  onCancel: () => void;
}

// The question Force asks before removing now; it says so when deleting is turned off. Shown only while asking.
function ForceRemovalConfirm({
  onConfirm,
  onCancel,
}: ForceRemovalConfirmProps) {
  const deletesFiles = useDeletesFiles();

  return (
    <ConfirmModal
      isOpen={true}
      kind={kinds.DANGER}
      title={translate('PortalActionForceRemoval')}
      message={forceRemovalMessage(deletesFiles)}
      confirmLabel={translate('PortalActionForceRemoval')}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

export default ForceRemovalConfirm;
