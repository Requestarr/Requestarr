import React, { useCallback, useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { plainText } from '../portalText';
import { AccessState, ContentType, PortalMember } from '../PortalTypes';
import usePortal from '../usePortal';
import usePortalAction from '../usePortalAction';
import TypeAccessFields, { DEFAULT_DAYS, TypeAccess } from './TypeAccessFields';

type Changes = Partial<Record<ContentType, Partial<TypeAccess>>>;

// What changes of one type, as it's saved: null leaves it.
interface AccessChange {
  contentType: ContentType;
  state: AccessState | null;
  limit: number | null;
  days: number | null;
}

// What the member has for the type now.
function savedAccess(member: PortalMember, type: ContentType): TypeAccess {
  const access = member.access.find((a) => a.contentType === type);

  return {
    state: access?.state ?? 'off',
    limit: access?.quotaLimit ?? 0,
    days: access?.quotaDays ?? DEFAULT_DAYS,
  };
}

// Only what differs from what they have: an untouched type isn't saved again. Days mean nothing without a limit.
function changeOf(
  type: ContentType,
  saved: TypeAccess,
  picked: TypeAccess
): AccessChange | undefined {
  const isLimitChanged =
    picked.limit !== saved.limit ||
    (picked.limit > 0 && picked.days !== saved.days);

  if (picked.state === saved.state && !isLimitChanged) {
    return undefined;
  }

  return {
    contentType: type,
    state: picked.state === saved.state ? null : picked.state,
    limit: isLimitChanged ? picked.limit : null,
    days: isLimitChanged ? picked.days : null,
  };
}

interface EditAccessModalContentProps {
  member: PortalMember;

  // After each answer, done or refused: a refused change doesn't stop the others.
  onSaved: () => void;
  onModalClose: () => void;
}

function EditAccessModalContent({
  member,
  onSaved,
  onModalClose,
}: EditAccessModalContentProps) {
  const { data: types, hasError } = usePortal<ContentType[]>('types');
  const [changes, setChanges] = useState<Changes>({});
  const { act, isBusy, outcome } = usePortalAction(onSaved);
  const isDone = outcome?.isDone;
  const onChange = useCallback(
    (type: ContentType, changed: Partial<TypeAccess>) =>
      setChanges((current) => ({
        ...current,
        [type]: { ...current[type], ...changed },
      })),
    []
  );
  const onSavePress = useCallback(() => {
    const changed = (types ?? []).flatMap((type) => {
      const saved = savedAccess(member, type);

      return changeOf(type, saved, { ...saved, ...changes[type] }) ?? [];
    });

    if (changed.length) {
      act(`members/${member.id}/access`, changed, 'PUT');
    } else {
      onModalClose();
    }
  }, [types, member, changes, act, onModalClose]);

  useEffect(() => {
    if (isDone) {
      onModalClose();
    }
  }, [isDone, onModalClose]);

  return (
    <ModalContent onModalClose={onModalClose}>
      <ModalHeader>{translate('PortalActionChangeAccess')}</ModalHeader>

      <ModalBody>
        {!types && !hasError && <LoadingIndicator />}

        {hasError && (
          <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
        )}

        {outcome && !outcome.isDone && (
          <Alert kind={kinds.DANGER}>{plainText(outcome.message)}</Alert>
        )}

        {types?.map((type) => (
          <TypeAccessFields
            key={type}
            type={type}
            access={{ ...savedAccess(member, type), ...changes[type] }}
            onChange={onChange}
          />
        ))}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onModalClose}>{translate('Cancel')}</Button>
        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isBusy}
          isDisabled={!types || isBusy}
          onPress={onSavePress}
        >
          {translate('Save')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

interface EditAccessModalProps extends EditAccessModalContentProps {
  isOpen: boolean;
}

// A member's access and limit for each type, saved together, as Sonarr main's Series/Edit/EditSeriesModalContent edits a
// series.
function EditAccessModal({ isOpen, ...props }: EditAccessModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={props.onModalClose}>
      {isOpen && <EditAccessModalContent {...props} />}
    </Modal>
  );
}

export default EditAccessModal;
