import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../accessLevelChoices';
import ActionModal from '../Components/ActionModal';
import { contentTypeText } from '../portalText';
import { AccessState, ContentType, PortalMember } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Requests/BulkActionModal.css';

// A type left as each member has it now.
const KEEP = 'keep';

export interface AccessChange {
  contentType: ContentType;
  state: AccessState;
}

// The types changed, without those left as they are.
function changesOf(picked: Record<string, string>): AccessChange[] {
  return Object.entries(picked)
    .filter(([, state]) => state !== KEEP)
    .map(([contentType, state]) => ({
      contentType: contentType as ContentType,
      state: state as AccessState,
    }));
}

interface ChangeAccessModalProps {
  members: PortalMember[];
  onConfirm: (changes: AccessChange[]) => void;
  onClose: () => void;
}

// Change access for one member or several, per content type, as their page sets it; each type starts as "leave as it
// is", and the members it changes are listed. The caller opens a fresh one each time (by key).
function ChangeAccessModal({
  members,
  onConfirm,
  onClose,
}: ChangeAccessModalProps) {
  const { data: types } = usePortal<ContentType[]>('types');
  const [picked, setPicked] = useState<Record<string, string>>({});
  const choices = [
    { key: KEEP, value: translate('PortalLeaveAsItIs') },
    ...accessLevelChoices(),
  ];
  const onChange = useCallback(
    ({ name, value }: { name: string; value: string }) =>
      setPicked((current) => ({ ...current, [name]: value })),
    []
  );
  const onConfirmPress = useCallback(
    () => onConfirm(changesOf(picked)),
    [picked, onConfirm]
  );

  return (
    <ActionModal
      isOpen={members.length > 0}
      title={translate('PortalActionChangeAccess')}
      confirmLabel={translate('PortalActionChangeAccess')}
      onConfirm={onConfirmPress}
      onClose={onClose}
    >
      <ul className={styles.titles}>
        {members.map((member) => (
          <li key={member.id}>{member.name}</li>
        ))}
      </ul>
      {(types ?? []).map((type) => (
        <FormGroup key={type}>
          <FormLabel>{contentTypeText(type)}</FormLabel>
          <FormInputGroup
            type={inputTypes.SELECT}
            name={type}
            value={picked[type] ?? KEEP}
            values={choices}
            onChange={onChange}
          />
        </FormGroup>
      ))}
    </ActionModal>
  );
}

export default ChangeAccessModal;
