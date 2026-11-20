import React, { useCallback } from 'react';
import { useHistory } from 'react-router-dom';
import FieldSet from 'Components/FieldSet';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
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
// Requests lists offers the same. A deleted record has no page left, so Delete record goes back to Requests.
function RequestActions({ detail, onDone }: RequestActionsProps) {
  const history = useHistory();
  const onRecordDeleted = useCallback(
    () => history.push(getPathWithUrlBase('/requests')),
    [history]
  );
  const { run, isBusy, outcome, dialogs } = useRequestActionRunner(
    onDone,
    onRecordDeleted
  );
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
