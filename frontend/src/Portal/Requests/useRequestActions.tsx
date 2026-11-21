import React, { useCallback, useMemo } from 'react';
import { useDispatch } from 'react-redux';
import { useHistory } from 'react-router-dom';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { messageTypes } from 'Helpers/Props';
import { showMessage } from 'Store/Actions/appActions';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import { toolbarActions } from '../Components/Select/selectActions';
import { PortalRequestDetail } from '../PortalTypes';
import useRequestActionRunner from './useRequestActionRunner';

// How long the sidebar says a removed request left the lists.
const GONE_MESSAGE_SECONDS = 6;

// What an admin can do to the request now, as its feed card in Discord offers, through the same rules, as toolbar
// buttons, as Sonarr main's SeriesDetails has its actions; its row in the Requests list's toolbar offers the same when
// it's picked. A request Sonarr and Radarr don't have is gone once Force removes it, so that goes back to Requests and
// says so in the sidebar's messages. The buttons are returned for the page's toolbar section to hold directly.
function useRequestActions(detail: PortalRequestDetail, onDone: () => void) {
  const history = useHistory();
  const dispatch = useDispatch();
  const { request } = detail;
  const onRequestGone = useCallback(() => {
    dispatch(
      showMessage({
        id: 'requestGone',
        name: 'requestGone',
        message: translate('PortalRequestRemovedFromLists', {
          title: request.title,
        }),
        type: messageTypes.SUCCESS,
        hideAfter: GONE_MESSAGE_SECONDS,
      })
    );
    history.push(getPathWithUrlBase('/requests'));
  }, [dispatch, history, request.title]);
  const { run, isBusy, dialogs } = useRequestActionRunner(
    onDone,
    onRequestGone
  );

  const buttons = useMemo(
    () =>
      toolbarActions(
        request.actions,
        [request],
        (item) => item.actions,
        (name) => run(request, name)
      ).map((action) => (
        <PageToolbarButton
          key={action.name}
          label={action.label}
          iconName={action.iconName}
          isDisabled={isBusy}
          onPress={action.onPress}
        />
      )),
    [request, run, isBusy]
  );

  return { buttons, dialogs };
}

export default useRequestActions;
