import React, { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import runBulkAction from '../Components/runBulkAction';
import { PortalMember } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import showOutcome, { ActionOutcome } from '../showOutcome';
import ChangeAccessModal, { AccessChange } from './ChangeAccessModal';

interface Asking {
  action: string;
  members: PortalMember[];

  // From the bar along the bottom in select mode, not a row's own button.
  fromBar: boolean;
}

// Where a result goes: the sidebar's messages, as Servarr reports a finished command, naming anyone it couldn't do;
// then onDone for a row's action, onBarDone for the select bar's.
function useReport(onDone: () => void, onBarDone: () => void) {
  const dispatch = useDispatch();

  return useCallback(
    (result: ActionOutcome, fromBar: boolean) => {
      showOutcome(dispatch, fromBar ? 'memberBulk' : 'portalAction', result);
      (fromBar ? onBarDone : onDone)();
    },
    [dispatch, onDone, onBarDone]
  );
}

// Runs a member action from their row (run: suspend and unsuspend at once) or from the select bar (ask: always asks
// first, naming everyone picked); remove after asking; change access in its dialog.
function useMemberActions(onDone: () => void, onBarDone: () => void = onDone) {
  const [asking, setAsking] = useState<Asking>();
  const [isBusy, setIsBusy] = useState(false);
  const report = useReport(onDone, onBarDone);

  const perform = useCallback(
    (job: Asking, changes: AccessChange[] = []) => {
      setAsking(undefined);
      setIsBusy(true);
      runBulkAction(job.members, {
        path: '/portal/members/bulk',
        body: { action: job.action, changes },
        label: actionLabel(job.action),
        nameOf: (member) => member.name ?? String(member.id),
      }).then((result) => {
        setIsBusy(false);
        report(result, job.fromBar);
      });
    },
    [report]
  );

  const run = useCallback(
    (members: PortalMember[], action: string) => {
      if (action.endsWith('suspend')) {
        perform({ action, members, fromBar: false });
      } else {
        setAsking({ action, members, fromBar: false });
      }
    },
    [perform]
  );
  const ask = useCallback(
    (members: PortalMember[], action: string) =>
      setAsking({ action, members, fromBar: true }),
    []
  );

  const onClose = useCallback(() => setAsking(undefined), []);
  const onConfirm = useCallback(
    () => asking && perform(asking),
    [asking, perform]
  );
  const onChangeAccess = useCallback(
    (changes: AccessChange[]) =>
      asking && perform({ ...asking, action: 'changeAccess' }, changes),
    [asking, perform]
  );
  const isChanging = asking?.action === 'changeAccess';
  const confirmLabel = actionLabel(asking?.action ?? 'suspend');

  const dialogs = (
    <>
      <ChangeAccessModal
        key={asking?.members.map((member) => member.id).join() ?? 'none'}
        members={isChanging ? asking.members : []}
        onConfirm={onChangeAccess}
        onClose={onClose}
      />
      <ConfirmModal
        isOpen={!!asking && !isChanging}
        kind={kinds.DANGER}
        title={confirmLabel}
        message={translate('PortalMembersConfirm', {
          action: confirmLabel,
          names: asking?.members.map((member) => member.name).join(', ') ?? '',
        })}
        confirmLabel={confirmLabel}
        onConfirm={onConfirm}
        onCancel={onClose}
      />
    </>
  );

  return { run, ask, isBusy, dialogs };
}

export default useMemberActions;
