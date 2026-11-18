import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../accessLevelChoices';
import ActionModal from '../Components/ActionModal';
import { contentTypeText } from '../portalText';
import { AccessState, ContentType, PortalAccessRequest } from '../PortalTypes';

interface ApproveAccessModalProps {
  asked?: PortalAccessRequest;
  types: ContentType[];
  onApprove: (
    levels: { contentType: ContentType; state: AccessState }[]
  ) => void;
  onClose: () => void;
}

// Approve: the real access for each type, as Discord's chooser asks; what they asked for starts as needing approval.
function ApproveAccessModal({
  asked,
  types,
  onApprove,
  onClose,
}: ApproveAccessModalProps) {
  const [chosen, setChosen] = useState<
    Partial<Record<ContentType, AccessState>>
  >({});
  const levels = accessLevelChoices();
  const levelOf = useCallback(
    (type: ContentType): AccessState =>
      chosen[type] ??
      (asked?.contentTypes.includes(type) ? 'needsApproval' : 'off'),
    [asked, chosen]
  );

  const onChange = useCallback(
    ({ name, value }: { name: string; value: AccessState }) => {
      setChosen((current) => ({ ...current, [name]: value }));
    },
    []
  );
  const onConfirm = useCallback(() => {
    onApprove(
      types.map((contentType) => ({ contentType, state: levelOf(contentType) }))
    );
    setChosen({});
  }, [levelOf, types, onApprove]);

  return (
    <ActionModal
      isOpen={!!asked}
      title={translate('PortalApproveAccessTitle', { name: asked?.name ?? '' })}
      confirmLabel={translate('PortalActionApprove')}
      onConfirm={onConfirm}
      onClose={onClose}
    >
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
    </ActionModal>
  );
}

export default ApproveAccessModal;
