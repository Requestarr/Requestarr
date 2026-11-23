import React, { useCallback, useEffect, useState } from 'react';
import { useDispatch } from 'react-redux';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { inputTypes, kinds } from 'Helpers/Props';
import { fetchMediaManagers } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import { callApi } from '../../requestarrApi';
import useApiAction from '../../useApiAction';

// What deleting it would do (InstanceDeletionPreview on the server).
interface DeletionPreview {
  name: string;
  implementation: string;
  openRequests: number;
  isMain: boolean;
  takeOver: { id: number; name: string }[];
}

interface DeleteResult {
  webhookProblem?: string;
}

// The confirm's words, in plain terms: what stays, what goes, the requests still waiting and who takes over.
function DeletionMessage(props: {
  preview: DeletionPreview;
  takeOver?: number;
  onTakeOver: (id: number) => void;
}) {
  const { preview, takeOver, onTakeOver } = props;
  const { name, implementation: app } = preview;
  const onChange = useCallback(
    ({ value }: { value: number }) => onTakeOver(value),
    [onTakeOver]
  );

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
      {preview.isMain && preview.takeOver.length > 0 && (
        <FormGroup>
          <FormLabel>{translate('InstanceDeleteTakeOver', { app })}</FormLabel>
          <FormInputGroup
            type={inputTypes.SELECT}
            name="takeOver"
            value={takeOver}
            values={preview.takeOver.map((o) => ({ key: o.id, value: o.name }))}
            onChange={onChange}
          />
        </FormGroup>
      )}
      {preview.isMain && !preview.takeOver.length && (
        <Alert kind={kinds.WARNING}>
          {translate('InstanceDeleteLast', { name, app })}
        </Alert>
      )}
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
// delete would do first, so it can say how many requests are still waiting and ask which instance takes over.
function DeleteMediaManagerModal(props: DeleteMediaManagerModalProps) {
  const { id, isOpen, onDeleted, onCancel } = props;
  const dispatch = useDispatch();
  const [preview, setPreview] = useState<DeletionPreview>();
  const [takeOver, setTakeOver] = useState<number>();
  const action = useApiAction();
  const { run } = action;

  useEffect(() => {
    if (isOpen) {
      callApi<DeletionPreview>(`/mediamanager/${id}/deletion`).then((found) => {
        setPreview(found);
        setTakeOver(found.takeOver[0]?.id);
      });
    }
  }, [id, isOpen]);

  const onConfirm = useCallback(() => {
    const query = takeOver ? `?takeOver=${takeOver}` : '';

    run(
      () => callApi<DeleteResult>(`/mediamanager/${id}${query}`, 'DELETE'),
      (result) => {
        dispatch(fetchMediaManagers());
        onDeleted(result.webhookProblem);
      }
    );
  }, [run, id, takeOver, dispatch, onDeleted]);

  return (
    <ConfirmModal
      isOpen={isOpen && !!preview}
      kind={kinds.DANGER}
      title={translate('InstanceDeleteTitle', { name: preview?.name ?? '' })}
      message={
        preview ? (
          <>
            <DeletionMessage
              preview={preview}
              takeOver={takeOver}
              onTakeOver={setTakeOver}
            />
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
