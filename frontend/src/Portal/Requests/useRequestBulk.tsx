import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch } from 'react-redux';
import { useSelect } from 'App/SelectContext';
import { toolbarActions, usePicked } from '../Components/Select/selectActions';
import { PortalRequest } from '../PortalTypes';
import showOutcome from '../showOutcome';
import BulkActionModal from './BulkActionModal';
import runBulk, { BULK_ACTIONS } from './runBulk';

const GROUP = BULK_ACTIONS;

// The picks that allow the action get it. One: it runs as on the request's own page (runOne). Several: it asks first
// with the list of their titles, then is done to each in turn; what happened shows in the sidebar's messages, as
// Servarr reports a finished command, and the picks clear.
function useRequestBulk(
  requests: PortalRequest[],
  runOne: (request: PortalRequest, action: string) => void,
  onDone: () => void
) {
  const [, selectDispatch] = useSelect();
  const [asking, setAsking] = useState<{
    name: string;
    requests: PortalRequest[];
  }>();
  const [isBusy, setIsBusy] = useState(false);
  const dispatch = useDispatch();
  const picked = usePicked(requests);

  const onAction = useCallback(
    (name: string, able: PortalRequest[]) =>
      able.length === 1
        ? runOne(able[0], name)
        : setAsking({ name, requests: able }),
    [runOne]
  );
  const actions = useMemo(
    () => toolbarActions(GROUP, picked, (r) => r.actions, onAction),
    [picked, onAction]
  );

  const onClose = useCallback(() => setAsking(undefined), []);
  const onConfirm = useCallback(
    (body: object) => {
      if (!asking) {
        return;
      }

      setAsking(undefined);
      setIsBusy(true);
      runBulk(asking.name, asking.requests, body).then((result) => {
        setIsBusy(false);
        showOutcome(dispatch, 'requestBulk', result);
        selectDispatch({ type: 'unselectAll' });
        onDone();
      });
    },
    [asking, selectDispatch, dispatch, onDone]
  );

  const modal = (
    <BulkActionModal
      action={asking?.name}
      requests={asking?.requests ?? []}
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );

  return { actions, isBusy, modal };
}

export default useRequestBulk;
