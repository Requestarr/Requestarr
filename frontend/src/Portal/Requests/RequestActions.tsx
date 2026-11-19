import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import RemovalActions from '../Components/RemovalActions';
import { PortalRequestDetail } from '../PortalTypes';
import usePortalAction from '../usePortalAction';
import ActionButton from './ActionButton';
import RequestActionDialogs, { RequestDialog } from './RequestActionDialogs';
import styles from '../Portal.css';

// Actions done at once, by the path after the request's own.
const AT_ONCE: Record<string, string> = {
  approve: 'approve',
  available: 'available',
  searchAgain: 'search-again',
  retry: 'retry',
};

// Actions that ask something first, and the dialog that asks.
const ASKING: Record<string, RequestDialog> = {
  deny: 'deny',
  cancel: 'cancel',
  addSeasons: 'seasons',
  remove: 'remove',
};

// An open removal's actions, which come last in the request's: its own buttons, as on the Queue page.
const REMOVAL_ACTIONS = ['forceRemoval', 'deletedByHand', 'dropRemoval'];

interface RequestActionsProps {
  detail: PortalRequestDetail;
  onDone: () => void;
}

// What an admin can do to the request now, as its feed card in Discord offers, through the same rules.
function RequestActions({ detail, onDone }: RequestActionsProps) {
  const action = usePortalAction(onDone);
  const { act, isBusy, outcome } = action;
  const [dialog, setDialog] = useState<RequestDialog>();
  const request = `requests/${detail.request.id}`;
  const removal = detail.actions.includes('dropRemoval')
    ? detail.latestRemoval
    : null;
  const own = detail.actions.filter((name) => !REMOVAL_ACTIONS.includes(name));

  const onClose = useCallback(() => setDialog(undefined), []);

  const answer = useCallback(
    (path: string, body?: unknown) => {
      setDialog(undefined);
      act(path, body);
    },
    [act]
  );

  const onPress = useCallback(
    (name: string) => {
      if (ASKING[name]) {
        setDialog(ASKING[name]);
      } else {
        act(`${request}/${AT_ONCE[name]}`);
      }
    },
    [act, request]
  );

  const onReason = useCallback(
    (which: 'deny' | 'cancel', reason: string) =>
      answer(`${request}/${which}`, { reason }),
    [answer, request]
  );
  const onSeasons = useCallback(
    (seasons: number[], future: boolean) =>
      answer(`${request}/seasons`, { seasons, all: false, future }),
    [answer, request]
  );
  const onRemove = useCallback(
    (seasons: number[], force: boolean) =>
      answer(`${request}/removal`, { seasons, force }),
    [answer, request]
  );

  if (!detail.actions.length) {
    return null;
  }

  return (
    <FieldSet legend={translate('PortalActions')}>
      <div className={styles.actions}>
        {own.map((name) => (
          <ActionButton
            key={name}
            action={name}
            isBusy={isBusy}
            onPress={onPress}
          />
        ))}
        {removal && (
          <RemovalActions
            removalId={removal.id}
            action={action}
            byHand={removal.status === 'deleteByHand'}
          />
        )}
      </div>

      <ActionOutcomeAlert outcome={outcome} />

      <RequestActionDialogs
        dialog={dialog}
        onReason={onReason}
        onSeasons={onSeasons}
        onRemove={onRemove}
        onClose={onClose}
      />
    </FieldSet>
  );
}

export default RequestActions;
