import React, { useCallback, useMemo, useState } from 'react';
import { BarAction } from '../Components/Selection/SelectionBar';
import { PortalRequest } from '../PortalTypes';
import { ActionOutcome } from '../usePortalAction';
import { actionLabel } from './ActionButton';
import BulkActionModal from './BulkActionModal';
import runBulk, { BULK_PATHS } from './runBulk';

// Removing a title and deleting a record are red; approving and denying look like the app's other buttons.
const DESTRUCTIVE = ['remove', 'deleteRecord'];

// What the selection bar offers for the requests picked (Approve, Deny, Start removal, Delete record), each asking
// first with the list of titles, then done to each in turn.
function useRequestBulk(picked: PortalRequest[], onDone: () => void) {
  const [asking, setAsking] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);
  const [outcome, setOutcome] = useState<ActionOutcome>();

  const actions: BarAction[] = useMemo(
    () =>
      Object.keys(BULK_PATHS).map((name) => ({
        name,
        label: actionLabel(name),
        isDanger: DESTRUCTIVE.includes(name),
        isOffered: picked.every((request) => request.actions.includes(name)),
      })),
    [picked]
  );
  const onClose = useCallback(() => setAsking(undefined), []);
  const onConfirm = useCallback(
    (body: object) => {
      setAsking(undefined);
      setIsBusy(true);
      runBulk(asking ?? '', picked, body).then((result) => {
        setIsBusy(false);
        setOutcome(result);
        onDone();
      });
    },
    [asking, picked, onDone]
  );

  const modal = (
    <BulkActionModal
      action={asking}
      requests={picked}
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );

  return { actions, onAction: setAsking, isBusy, outcome, modal };
}

export default useRequestBulk;
