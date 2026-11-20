import React, { useCallback } from 'react';
import { useDispatch } from 'react-redux';
import { useHistory } from 'react-router-dom';
import FieldSet from 'Components/FieldSet';
import { messageTypes } from 'Helpers/Props';
import { showMessage } from 'Store/Actions/appActions';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import { PortalRequestDetail } from '../PortalTypes';
import ActionButton from './ActionButton';
import useRequestActionRunner from './useRequestActionRunner';
import styles from '../Portal.css';

// How long the sidebar says a removed request left the lists.
const GONE_MESSAGE_SECONDS = 6;

interface RequestActionsProps {
  detail: PortalRequestDetail;
  onDone: () => void;
}

// What an admin can do to the request now, as its feed card in Discord offers, through the same rules; its row in the
// Requests list's toolbar offers the same when it's picked. A request Sonarr and Radarr don't have is gone once Force
// removes it, so that goes back to Requests and says so in the sidebar's messages.
function RequestActions({ detail, onDone }: RequestActionsProps) {
  const history = useHistory();
  const dispatch = useDispatch();
  const { title } = detail.request;
  const onRequestGone = useCallback(() => {
    dispatch(
      showMessage({
        id: 'requestGone',
        name: 'requestGone',
        message: translate('PortalRequestRemovedFromLists', { title }),
        type: messageTypes.SUCCESS,
        hideAfter: GONE_MESSAGE_SECONDS,
      })
    );
    history.push(getPathWithUrlBase('/requests'));
  }, [dispatch, history, title]);
  const { run, isBusy, outcome, dialogs } = useRequestActionRunner(
    onDone,
    onRequestGone
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
