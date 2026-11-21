import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../accessLevelChoices';
import defaultAccess from '../defaultAccess';
import { contentTypeText } from '../portalText';
import {
  AccessState,
  ContentType,
  PortalAccessRequest,
  PortalInstance,
} from '../PortalTypes';
import usePortal from '../usePortal';

interface ApproveAccessModalProps {
  asked?: PortalAccessRequest;
  types: ContentType[];
  onApprove: (
    levels: { contentType: ContentType; state: AccessState }[]
  ) => void;
  onClose: () => void;
}

// Approve: the real access for each type, as Discord's chooser asks; what they asked for starts at its media manager's
// default access for new members.
function ApproveAccessModal({
  asked,
  types,
  onApprove,
  onClose,
}: ApproveAccessModalProps) {
  const { data: instances } = usePortal<PortalInstance[]>('instances');
  const [chosen, setChosen] = useState<
    Partial<Record<ContentType, AccessState>>
  >({});
  const levels = accessLevelChoices();
  const levelOf = useCallback(
    (type: ContentType): AccessState =>
      chosen[type] ??
      (asked?.contentTypes.includes(type)
        ? defaultAccess(type, instances ?? [])
        : 'off'),
    [asked, chosen, instances]
  );

  const onChange = useCallback(
    ({ name, value }: { name: string; value: AccessState }) => {
      setChosen((current) => ({ ...current, [name]: value }));
    },
    []
  );
  const onModalClose = useCallback(() => {
    setChosen({});
    onClose();
  }, [onClose]);
  const onConfirm = useCallback(() => {
    // Enter confirms too, so nothing is approved until the choices it starts from have loaded.
    if (!instances) {
      return;
    }

    onApprove(
      types.map((contentType) => ({ contentType, state: levelOf(contentType) }))
    );
    setChosen({});
  }, [instances, levelOf, types, onApprove]);

  return (
    <ConfirmModal
      isOpen={!!asked}
      title={translate('PortalApproveAccessTitle', { name: asked?.name ?? '' })}
      confirmLabel={translate('PortalActionApprove')}
      isSpinning={!instances}
      kind={kinds.PRIMARY}
      cancelLabel={translate('Cancel')}
      message={
        <>
          {types.map((type) => (
            <FormGroup key={type}>
              <FormLabel>{contentTypeText(type)}</FormLabel>
              <FormInputGroup
                type={inputTypes.SELECT}
                name={type}
                value={levelOf(type)}
                values={levels}
                onChange={onChange}
              />
            </FormGroup>
          ))}
        </>
      }
      onConfirm={onConfirm}
      onCancel={onModalClose}
    />
  );
}

export default ApproveAccessModal;
