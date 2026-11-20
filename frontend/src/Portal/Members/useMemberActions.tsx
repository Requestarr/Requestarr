import React, { useCallback, useState } from 'react';
import { useHistory } from 'react-router-dom';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import runEach from '../Components/Selection/runEach';
import { PortalMember } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import { ActionOutcome } from '../usePortalAction';
import ChangeAccessModal, { AccessChange } from './ChangeAccessModal';

interface Asking {
  action: string;
  members: PortalMember[];
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

// Runs a member action from their row (run: suspend and unsuspend at once) or from the selection bar (ask: always asks
// first, naming everyone picked); remove after asking; change access in its dialog; onboard on Add member's setup
// step. One outcome for the last action, which names anyone it couldn't do.
function useMemberActions(onDone: () => void) {
  const history = useHistory();
  const [asking, setAsking] = useState<Asking>();
  const [isBusy, setIsBusy] = useState(false);
  const [outcome, setOutcome] = useState<ActionOutcome>();

  const perform = useCallback(
    (action: string, members: PortalMember[], changes: AccessChange[] = []) => {
      setAsking(undefined);
      setIsBusy(true);
      runEach(members, {
        label: actionLabel(action),
        nameOf: (member) => member.name,
        call: callFor(action, changes),
      }).then((result) => {
        setIsBusy(false);
        setOutcome(result);
        onDone();
      });
    },
    [onDone]
  );

  const run = useCallback(
    (members: PortalMember[], action: string) => {
      if (action === 'onboard') {
        history.push(
          getPathWithUrlBase(`/members/add?pick=${members[0].discordId}`)
        );
      } else if (action.endsWith('suspend')) {
        perform(action, members);
      } else {
        setAsking({ action, members });
      }
    },
    [history, perform]
  );
  const ask = useCallback(
    (members: PortalMember[], action: string) => setAsking({ action, members }),
    []
  );

  const onClose = useCallback(() => setAsking(undefined), []);
  const onConfirm = useCallback(
    () => asking && perform(asking.action, asking.members),
    [asking, perform]
  );
  const onChangeAccess = useCallback(
    (changes: AccessChange[]) =>
      asking && perform('changeAccess', asking.members, changes),
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

  return { run, ask, isBusy, outcome, dialogs };
}

export default useMemberActions;
