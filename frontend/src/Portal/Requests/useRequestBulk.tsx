import React, { useCallback, useMemo, useState } from 'react';
import translate from 'Utilities/String/translate';
import { BarAction } from '../Components/Selection/SelectionBar';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { ActionOutcome } from '../usePortalAction';
import { actionLabel } from './ActionButton';
import BulkActionModal from './BulkActionModal';
import runBulk, { BULK_PATHS } from './runBulk';

// Titles a disabled action names on hover, before "and 4 more".
const NAMED = 3;

// Why an action is off for what's picked: the requests that don't offer it now. Null when every one does.
function whyNot(picked: PortalRequest[], action: string) {
  const lacking = picked.filter((request) => !request.actions.includes(action));

  if (!lacking.length) {
    return null;
  }

  const named = lacking.slice(0, NAMED).map(titleText).join(', ');
  const titles =
    lacking.length > NAMED
      ? translate('PortalAndMoreNamed', {
          names: named,
          count: lacking.length - NAMED,
        })
      : named;

  return translate('PortalNotOffered', { action: actionLabel(action), titles });
}

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
        isDanger: name !== 'approve',
        whyNot: whyNot(picked, name),
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
