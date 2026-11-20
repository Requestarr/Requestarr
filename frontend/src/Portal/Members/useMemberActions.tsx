import React, { useCallback, useState } from 'react';
import { useDispatch } from 'react-redux';
import { useHistory } from 'react-router-dom';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds, messageTypes } from 'Helpers/Props';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import { showMessage } from 'Store/Actions/appActions';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import runEach from '../Components/runEach';
import { plainText } from '../portalText';
import { PortalMember } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import { ActionOutcome } from '../usePortalAction';
import ChangeAccessModal, { AccessChange } from './ChangeAccessModal';

interface Asking {
  action: string;
  members: PortalMember[];

  // From the bar along the bottom in select mode, not a row's own button.
  fromBar: boolean;
}

// How long the bar's result stays in the sidebar, as Servarr keeps a finished command's: longer when something failed.
const MESSAGE_SECONDS = 4;
const FAILURE_SECONDS = 10;

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

// Where a result goes: a row's on the page (outcome), then onDone; the bar's in the sidebar's messages, as Servarr
// reports a finished command, then onBarDone. Either names anyone it couldn't do.
function useReport(onDone: () => void, onBarDone: () => void) {
  const dispatch = useDispatch();
  const [outcome, setOutcome] = useState<ActionOutcome>();

  const report = useCallback(
    (result: ActionOutcome, fromBar: boolean) => {
      if (!fromBar) {
        setOutcome(result);
        onDone();
        return;
      }

      dispatch(
        showMessage({
          id: 'memberBulk',
          name: 'memberBulk',
          message: plainText(result.message),
          type: result.isDone ? messageTypes.SUCCESS : messageTypes.ERROR,
          hideAfter: result.isDone ? MESSAGE_SECONDS : FAILURE_SECONDS,
        })
      );
      onBarDone();
    },
    [dispatch, onDone, onBarDone]
  );

  return { outcome, report };
}

// Runs a member action from their row (run: suspend and unsuspend at once) or from the select bar (ask: always asks
// first, naming everyone picked); remove after asking; change access in its dialog; onboard on Add member's setup
// step.
function useMemberActions(onDone: () => void, onBarDone: () => void = onDone) {
  const history = useHistory();
  const [asking, setAsking] = useState<Asking>();
  const [isBusy, setIsBusy] = useState(false);
  const { outcome, report } = useReport(onDone, onBarDone);

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
        history.push(
          getPathWithUrlBase(`/members/add?pick=${members[0].discordId}`)
        );
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

  return { run, ask, isBusy, outcome, dialogs };
}

export default useMemberActions;
