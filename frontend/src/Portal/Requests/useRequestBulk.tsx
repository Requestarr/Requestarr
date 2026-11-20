import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch } from 'react-redux';
import { useSelect } from 'App/SelectContext';
import { messageTypes } from 'Helpers/Props';
import { showMessage } from 'Store/Actions/appActions';
import { toolbarActions, usePicked } from '../Components/Select/selectActions';
import { plainText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import BulkActionModal from './BulkActionModal';
import runBulk, { BULK_PATHS } from './runBulk';

// How long the result stays in the sidebar, as Servarr keeps a finished command's: longer when something failed.
const MESSAGE_SECONDS = 4;
const FAILURE_SECONDS = 10;

const GROUP = Object.keys(BULK_PATHS);

// One pick: the action runs as on the request's own page (runOne). Several: it asks first with the list of titles,
// then is done to each in turn; what happened shows in the sidebar's messages, as Servarr reports a finished
// command, and the picks clear.
function useRequestBulk(
  requests: PortalRequest[],
  runOne: (request: PortalRequest, action: string) => void,
  onDone: () => void
) {
  const [, selectDispatch] = useSelect();
  const [asking, setAsking] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);
  const dispatch = useDispatch();
  const picked = usePicked(requests);

  const onAction = useCallback(
    (name: string) =>
      picked.length === 1 ? runOne(picked[0], name) : setAsking(name),
    [picked, runOne]
  );
  const actions = useMemo(
    () => toolbarActions(GROUP, picked, (r) => r.actions, onAction),
    [picked, onAction]
  );

  const onClose = useCallback(() => setAsking(undefined), []);
  const onConfirm = useCallback(
    (body: object) => {
      setAsking(undefined);
      setIsBusy(true);
      runBulk(asking ?? '', picked, body).then((result) => {
        setIsBusy(false);
        dispatch(
          showMessage({
            id: 'requestBulk',
            name: 'requestBulk',
            message: plainText(result.message),
            type: result.isDone ? messageTypes.SUCCESS : messageTypes.ERROR,
            hideAfter: result.isDone ? MESSAGE_SECONDS : FAILURE_SECONDS,
          })
        );
        selectDispatch({ type: 'unselectAll' });
        onDone();
      });
    },
    [asking, picked, selectDispatch, dispatch, onDone]
  );

  const modal = (
    <BulkActionModal
      action={asking}
      requests={picked}
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );

  return { actions, isBusy, modal };
}

export default useRequestBulk;
