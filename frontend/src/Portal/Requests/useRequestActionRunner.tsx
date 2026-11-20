import React, { useCallback, useState } from 'react';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import usePortalAction from '../usePortalAction';
import RequestActionDialogs, { RequestDialog } from './RequestActionDialogs';

// Actions done at once, by the path after the request's own.
const AT_ONCE: Record<string, string> = {
  approve: 'approve',
  available: 'available',
  searchAgain: 'search-again',
  retry: 'retry',
};

// An open removal's actions done at once, by the path after the removal's own.
const REMOVAL_AT_ONCE: Record<string, string> = {
  dropRemoval: 'drop',
  deletedByHand: 'deleted-by-hand',
};

// Actions that ask something first, and the dialog that asks.
const ASKING: Record<string, RequestDialog> = {
  deny: 'deny',
  cancel: 'cancel',
  addSeasons: 'seasons',
  remove: 'remove',
  forceRemoval: 'force',
};

interface Asking {
  request: PortalRequest;
  dialog: RequestDialog;
}

// Runs any of a request's actions by name, as its page and its row in a list offer them (the request's own list, from
// the same rules), asking first where Discord asks. One runner serves a whole list: its dialogs and the outcome are
// for the request acted on last; onDone follows every action.
function useRequestActionRunner(onDone: () => void) {
  const action = usePortalAction(onDone);
  const { act } = action;
  const [asking, setAsking] = useState<Asking>();
  const path = `requests/${asking?.request.id}`;

  const run = useCallback(
    (request: PortalRequest, name: string) => {
      if (ASKING[name]) {
        setAsking({ request, dialog: ASKING[name] });
      } else if (REMOVAL_AT_ONCE[name]) {
        act(`removals/${request.removalId}/${REMOVAL_AT_ONCE[name]}`);
      } else {
        act(`requests/${request.id}/${AT_ONCE[name]}`);
      }
    },
    [act]
  );

  const onClose = useCallback(() => setAsking(undefined), []);
  const answer = useCallback(
    (to: string, body?: unknown) => {
      setAsking(undefined);
      act(to, body);
    },
    [act]
  );
  const onReason = useCallback(
    (which: 'deny' | 'cancel', reason: string) =>
      answer(`${path}/${which}`, { reason }),
    [answer, path]
  );
  const onSeasons = useCallback(
    (seasons: number[], future: boolean) =>
      answer(`${path}/seasons`, { seasons, all: false, future }),
    [answer, path]
  );
  const onRemove = useCallback(
    (seasons: number[], force: boolean) =>
      answer(`${path}/removal`, { seasons, force }),
    [answer, path]
  );
  const onForce = useCallback(
    () => answer(`removals/${asking?.request.removalId}/force`),
    [answer, asking]
  );

  const dialogs = (
    <RequestActionDialogs
      key={asking?.request.id ?? 'none'}
      dialog={asking?.dialog}
      title={asking ? titleText(asking.request) : ''}
      isRecordOnly={asking?.request.status === 'notInLibrary'}
      onReason={onReason}
      onSeasons={onSeasons}
      onRemove={onRemove}
      onForce={onForce}
      onClose={onClose}
    />
  );

  return { ...action, run, dialogs };
}

export default useRequestActionRunner;
