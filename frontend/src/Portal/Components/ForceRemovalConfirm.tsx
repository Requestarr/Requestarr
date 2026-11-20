import React from 'react';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { actionHeading } from '../Requests/ActionButton';
import useDeletesFiles, { forceRemovalMessage } from '../useDeletesFiles';

interface ForceRemovalConfirmProps {
  // What it removes, when the dialog isn't on that title's own page.
  title?: string;
  onConfirm: () => void;
  onCancel: () => void;
}

// The question Force asks before removing now; it says so when deleting is turned off. Shown only while asking, once
// it's known whether deleting is on.
function ForceRemovalConfirm({
  title,
  onConfirm,
  onCancel,
}: ForceRemovalConfirmProps) {
  const deletesFiles = useDeletesFiles();

  return (
    <ConfirmModal
      isOpen={deletesFiles !== undefined}
      kind={kinds.DANGER}
      title={actionHeading('forceRemoval', title)}
      message={forceRemovalMessage(deletesFiles)}
      confirmLabel={translate('PortalActionForceRemoval')}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

export default ForceRemovalConfirm;
