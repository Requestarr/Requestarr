import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch } from 'react-redux';
import { useSelect } from 'App/SelectContext';
import { messageTypes } from 'Helpers/Props';
import { showMessage } from 'Store/Actions/appActions';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import { SelectAction } from '../Components/Select/SelectFooter';
import { plainText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { actionLabel } from './ActionButton';
import BulkActionModal from './BulkActionModal';
import runBulk, { BULK_PATHS } from './runBulk';

// Removing a title and deleting a record are red; approving and denying look like the app's other buttons.
const DESTRUCTIVE = ['remove', 'deleteRecord'];

// How long the result stays in the sidebar, as Servarr keeps a finished command's: longer when something failed.
const MESSAGE_SECONDS = 4;
const FAILURE_SECONDS = 10;

// What the select bar offers for the requests picked (Approve, Deny, Start removal, Delete record), each asking first
// with the list of titles, then done to each in turn. Once done the picks clear, as Prowlarr's do after a delete, and
// what happened shows in the sidebar's messages, as Servarr reports a finished command.
function useRequestBulk(requests: PortalRequest[], onDone: () => void) {
  const [selectState, selectDispatch] = useSelect();
  const [asking, setAsking] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);
  const dispatch = useDispatch();

  const picked = useMemo(() => {
    const ids = getSelectedIds(selectState.selectedState);

    return requests.filter((request) => ids.includes(request.id));
  }, [requests, selectState.selectedState]);

  const actions: SelectAction[] = useMemo(
    () =>
      Object.keys(BULK_PATHS).map((name) => ({
        name,
        label: actionLabel(name),
        isDanger: DESTRUCTIVE.includes(name),
        isOffered:
          picked.length > 0 &&
          picked.every((request) => request.actions.includes(name)),
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
        dispatch(
          showMessage({
            id: 'requestBulk',
            name: 'requestBulk',
            message: plainText(result.message),
            type: result.isDone ? messageTypes.SUCCESS : messageTypes.ERROR,
            hideAfter: result.isDone ? MESSAGE_SECONDS : FAILURE_SECONDS,
          })
        );
        selectDispatch({ type: 'unselectAll' });
        onDone();
      });
    },
    [asking, picked, selectDispatch, dispatch, onDone]
  );

  const modal = (
    <BulkActionModal
      action={asking}
      requests={picked}
      onConfirm={onConfirm}
      onClose={onClose}
    />
  );

  return {
    picked,
    actions,
    onAction: setAsking,
    isBusy,
    modal,
  };
}

export default useRequestBulk;
