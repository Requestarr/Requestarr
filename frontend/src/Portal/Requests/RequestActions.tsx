import React, { useCallback } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import { PortalRequestDetail } from '../PortalTypes';
import ActionButton from './ActionButton';
import useRequestActionRunner from './useRequestActionRunner';
import styles from '../Portal.css';

interface RequestActionsProps {
  detail: PortalRequestDetail;
  onDone: () => void;
}

// What an admin can do to the request now, as its feed card in Discord offers, through the same rules; its row in the
// Requests list's toolbar offers the same when it's picked.
function RequestActions({ detail, onDone }: RequestActionsProps) {
  const { run, isBusy, outcome, dialogs } = useRequestActionRunner(onDone);
  const { request } = detail;
  const onPress = useCallback(
    (name: string) => run(request, name),
    [run, request]
  );

  if (!request.actions.length) {
    return null;
  }

  return (
    <FieldSet legend={translate('PortalActions')}>
      <div className={styles.actions}>
        {request.actions.map((name) => (
          <ActionButton
            key={name}
            action={name}
            isBusy={isBusy}
            onPress={onPress}
          />
        ))}
      </div>

      <ActionOutcomeAlert outcome={outcome} />
      {dialogs}
    </FieldSet>
  );
}

export default RequestActions;
