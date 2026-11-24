import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import { fetchMediaManagers } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import { callApi } from '../../requestarrApi';
import useApiAction from '../../useApiAction';
import { allOtherStopsText } from './purpose';

// What deleting it would do (InstanceDeletionPreview on the server).
interface DeletionPreview {
  name: string;
  implementation: string;
  openRequests: number;
  takesAllOther: boolean;
  isLast: boolean;
}

interface DeleteResult {
  webhookProblem?: string;
}

// What stops when it's gone: everything of its type when it's the only one, else its type's other requests when it
// takes them (nothing hands them on by itself).
function stopsText(preview: DeletionPreview) {
  const { name, implementation: app, takesAllOther, isLast } = preview;

  if (isLast) {
    return translate('InstanceDeleteLast', { name, app });
  }

  return takesAllOther ? allOtherStopsText(app) : undefined;
}

// The confirm's words, in plain terms: what stays, what goes, the requests still waiting and what stops.
function DeletionMessage({ preview }: { preview: DeletionPreview }) {
  const { name } = preview;
  const stops = stopsText(preview);

  return (
    <>
      <p>{translate('InstanceDeleteMessage', { name })}</p>
      {preview.openRequests > 0 && (
        <p>
          {translate('InstanceDeleteOpenRequests', {
            count: preview.openRequests,
            name,
          })}
        </p>
      )}
      {stops && <Alert kind={kinds.WARNING}>{stops}</Alert>}
    </>
  );
}

interface DeleteMediaManagerModalProps {
  id: number;
  isOpen: boolean;
  onDeleted: (problem?: string) => void;
  onCancel: () => void;
}

// Servarr's delete confirm for a Sonarr or Radarr, as Sonarr's provider modals ask before Delete. It reads what the
// delete would do first, so it can say how many requests are still waiting and what stops.
function DeleteMediaManagerModal(props: DeleteMediaManagerModalProps) {
  const { id, isOpen, onDeleted, onCancel } = props;
  const dispatch = useDispatch();
  const [preview, setPreview] = useState<DeletionPreview>();
  const action = useApiAction();
  const { run } = action;

  useEffect(() => {
    if (isOpen) {
      callApi<DeletionPreview>(`/mediamanager/${id}/deletion`).then(setPreview);
    }
  }, [id, isOpen]);

  const onConfirm = useCallback(() => {
    run(
      () => callApi<DeleteResult>(`/mediamanager/${id}`, 'DELETE'),
      (result) => {
        dispatch(fetchMediaManagers());
        onDeleted(result.webhookProblem);
      }
    );
  }, [run, id, dispatch, onDeleted]);

  return (
    <ConfirmModal
      isOpen={isOpen && !!preview}
      kind={kinds.DANGER}
      title={translate('InstanceDeleteTitle', { name: preview?.name ?? '' })}
      message={
        preview ? (
          <>
            <DeletionMessage preview={preview} />
            {action.problem && (
              <Alert kind={kinds.DANGER}>{action.problem}</Alert>
            )}
          </>
        ) : (
          ''
        )
      }
      confirmLabel={translate('Delete')}
      isSpinning={action.isBusy}
      onConfirm={onConfirm}
      onCancel={onCancel}
    />
  );
}

export default DeleteMediaManagerModal;
