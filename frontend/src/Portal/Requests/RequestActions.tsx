import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
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
  forceRemoval: 'force',
};

interface RequestActionsProps {
  detail: PortalRequestDetail;
  onDone: () => void;
}

// What an admin can do to the request now, as its feed card in Discord offers, through the same rules.
function RequestActions({ detail, onDone }: RequestActionsProps) {
  const { act, isBusy, outcome } = usePortalAction(onDone);
  const [dialog, setDialog] = useState<RequestDialog>();
  const request = `requests/${detail.request.id}`;
  const removal = `removals/${detail.latestRemoval?.id}`;

  const onClose = useCallback(() => setDialog(undefined), []);

  const answer = useCallback(
    (path: string, body?: unknown) => {
      setDialog(undefined);
      act(path, body);
    },
    [act]
  );

  const onPress = useCallback(
    (action: string) => {
      if (ASKING[action]) {
        setDialog(ASKING[action]);
      } else if (action === 'dropRemoval') {
        act(`${removal}/drop`);
      } else {
        act(`${request}/${AT_ONCE[action]}`);
      }
    },
    [act, request, removal]
  );

  const onReason = useCallback(
    (which: 'deny' | 'cancel', reason: string) =>
      answer(`${request}/${which}`, { reason }),
    [answer, request]
  );
  const onSeasons = useCallback(
    (seasons: number[], all: boolean, future: boolean) =>
      answer(`${request}/seasons`, { seasons, all, future }),
    [answer, request]
  );
  const onRemove = useCallback(
    (seasons: number[], force: boolean) =>
      answer(`${request}/removal`, { seasons, force }),
    [answer, request]
  );
  const onForce = useCallback(
    () => answer(`${removal}/force`),
    [answer, removal]
  );

  if (!detail.actions.length) {
    return null;
  }

  return (
    <FieldSet legend={translate('PortalActions')}>
      <div className={styles.actions}>
        {detail.actions.map((action) => (
          <ActionButton
            key={action}
            action={action}
            isBusy={isBusy}
            onPress={onPress}
          />
        ))}
      </div>

      <ActionOutcomeAlert outcome={outcome} />

      <RequestActionDialogs
        dialog={dialog}
        onReason={onReason}
        onSeasons={onSeasons}
        onRemove={onRemove}
        onForce={onForce}
        onClose={onClose}
      />
    </FieldSet>
  );
}

export default RequestActions;
