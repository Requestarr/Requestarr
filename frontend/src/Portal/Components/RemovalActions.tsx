import React, { useCallback, useState } from 'react';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ActionButton from '../Requests/ActionButton';
import { PortalAction } from '../usePortalAction';

interface RemovalActionsProps {
  removalId: number | string;
  action: PortalAction;
}

// Force removal (after asking) and Drop removal, as an open removal's card in Discord has them.
function RemovalActions({ removalId, action }: RemovalActionsProps) {
  const [isForcing, setIsForcing] = useState(false);

  const onPress = useCallback(
    (name: string) => {
      if (name === 'forceRemoval') {
        setIsForcing(true);
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
      {['forceRemoval', 'dropRemoval'].map((name) => (
        <ActionButton
          key={name}
          action={name}
          isBusy={action.isBusy}
          onPress={onPress}
        />
      ))}
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
