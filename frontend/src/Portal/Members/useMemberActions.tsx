import React, { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
import { useHistory } from 'react-router-dom';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import runEach from '../Components/runEach';
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

// What each action does to one member, by the services their page and Discord's member card use.
function callFor(action: string, changes: AccessChange[]) {
  return (member: PortalMember) => {
    if (action === 'changeAccess') {
      return Promise.all(
        changes.map((change) =>
          callApi(`/portal/members/${member.id}/access`, 'PUT', {
            ...change,
            instanceId: null,
          })
        )
      );
    }

    const path = action === 'remove-member' ? 'remove' : action;

    return callApi(`/portal/members/${member.id}/${path}`, 'POST', {});
  };
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
// first, naming everyone picked); remove after asking; change access in its dialog; onboard on Add member's setup
// step.
function useMemberActions(onDone: () => void, onBarDone: () => void = onDone) {
  const history = useHistory();
  const [asking, setAsking] = useState<Asking>();
  const [isBusy, setIsBusy] = useState(false);
  const report = useReport(onDone, onBarDone);

  const perform = useCallback(
    (job: Asking, changes: AccessChange[] = []) => {
      setAsking(undefined);
      setIsBusy(true);
      runEach(job.members, {
        label: actionLabel(job.action),
        nameOf: (member) => member.name,
        call: callFor(job.action, changes),
      }).then((result) => {
        setIsBusy(false);
        report(result, job.fromBar);
      });
    },
    [report]
  );

  const run = useCallback(
    (members: PortalMember[], action: string) => {
      if (action === 'onboard') {
        const picks = members.map((member) => member.discordId).join(',');

        history.push(getPathWithUrlBase(`/members/add?pick=${picks}`));
      } else if (action.endsWith('suspend')) {
        perform({ action, members, fromBar: false });
      } else {
        setAsking({ action, members, fromBar: false });
      }
    },
    [history, perform]
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
