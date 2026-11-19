import React, { useCallback, useState } from 'react';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ActionButton from '../Requests/ActionButton';
import { PortalAction } from '../usePortalAction';

interface RemovalActionsProps {
  removalId: number | string;
  action: PortalAction;

  // Agreed while deleting files is off: the admin deletes it by hand and says so, instead of forcing it.
  byHand?: boolean;
}

// Force removal (after asking) and Drop removal, as an open removal's card in Discord has them; I deleted it instead
// of Force for one waiting to be deleted by hand.
function RemovalActions({ removalId, action, byHand }: RemovalActionsProps) {
  const [isForcing, setIsForcing] = useState(false);

  const onPress = useCallback(
    (name: string) => {
      if (name === 'forceRemoval') {
        setIsForcing(true);
      } else if (name === 'deletedByHand') {
        action.act(`removals/${removalId}/deleted-by-hand`);
      } else {
        action.act(`removals/${removalId}/drop`);
      }
    },
    [action, removalId]
  );
  const onForce = useCallback(() => {
    setIsForcing(false);
    action.act(`removals/${removalId}/force`);
  }, [action, removalId]);
  const onCancel = useCallback(() => setIsForcing(false), []);

  return (
    <>
      {[byHand ? 'deletedByHand' : 'forceRemoval', 'dropRemoval'].map(
        (name) => (
          <ActionButton
            key={name}
            action={name}
            isBusy={action.isBusy}
            onPress={onPress}
          />
        )
      )}
      <ConfirmModal
        isOpen={isForcing}
        kind={kinds.DANGER}
        title={translate('PortalActionForceRemoval')}
        message={translate('PortalForceRemovalMessage')}
        confirmLabel={translate('PortalActionForceRemoval')}
        onConfirm={onForce}
        onCancel={onCancel}
      />
    </>
  );
}

export default RemovalActions;
