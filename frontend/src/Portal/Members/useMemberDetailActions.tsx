import React, { useCallback, useState } from 'react';
import { useHistory } from 'react-router-dom';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import { icons, kinds } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import TextConfirmModal from '../Components/TextConfirmModal';
import { isImported } from '../portalText';
import {
  ContentType,
  PortalInstance,
  PortalMemberDetail,
} from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import usePortal from '../usePortal';
import usePortalAction, { PortalAction } from '../usePortalAction';
import AccessRow from './AccessRow';

type Dialog = 'remove' | 'plex' | 'access';

function plexPrompt() {
  return {
    title: translate('PortalActionLinkPlex'),
    label: translate('PortalPlexUsername'),
    helpText: translate('PortalPlexUsernameHelpText'),
    confirmLabel: translate('PortalActionLinkPlex'),
  };
}

interface EditAccessModalProps {
  detail: PortalMemberDetail;
  action: PortalAction;
  isOpen: boolean;
  onModalClose: () => void;
}

// Their access, instance and limit for each type, as Sonarr main's Series/Edit/EditSeriesModalContent edits a series;
// each type is saved on its own, as the member card in Discord does.
function EditAccessModal({
  detail,
  action,
  isOpen,
  onModalClose,
}: EditAccessModalProps) {
  const { data: instances } = usePortal<PortalInstance[]>('instances');
  const { data: offered } = usePortal<ContentType[]>('types');
  const { member } = detail;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{translate('PortalActionChangeAccess')}</ModalHeader>
        <ModalBody>
          {(offered ?? []).map((type) => (
            <AccessRow
              key={type}
              memberId={member.id}
              type={type}
              access={member.access.find((a) => a.contentType === type)}
              instances={(instances ?? []).filter(
                (i) => i.contentType === type
              )}
              action={action}
            />
          ))}
        </ModalBody>
        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

// The actions a member's card in Discord has, by its name, for the member's status: onboarding someone known only
// from an import; otherwise suspend or unsuspend, change access, link Plex, and remove. None for someone removed.
function actionsFor(detail: PortalMemberDetail) {
  const { status } = detail.member;

  if (status === 'removed') {
    return [];
  }

  if (isImported(status)) {
    return ['onboard'];
  }

  return [
    status === 'suspended' ? 'unsuspend' : 'suspend',
    'changeAccess',
    'link-plex',
    'remove-member',
  ];
}

const ICONS: Record<string, typeof icons.ADD> = {
  onboard: icons.ADD,
  suspend: icons.PAUSED,
  unsuspend: icons.RESTORE,
  changeAccess: icons.EDIT,
  'link-plex': icons.EXTERNAL_LINK,
  'remove-member': icons.DELETE,
};

const DIALOGS: Record<string, Dialog> = {
  changeAccess: 'access',
  'link-plex': 'plex',
  'remove-member': 'remove',
};

// A member's page actions as toolbar buttons, as Sonarr main's SeriesDetails has its own: suspend and unsuspend at
// once; change access, link Plex and remove in their windows; onboarding on Onboard's page with them picked. Returns the
// buttons for the page's toolbar section to hold directly, and the windows.
function useMemberDetailActions(
  detail: PortalMemberDetail,
  onDone: () => void
) {
  const history = useHistory();
  const action = usePortalAction(onDone);
  const [dialog, setDialog] = useState<Dialog>();
  const { member } = detail;
  const path = `members/${member.id}`;
  const onClose = useCallback(() => setDialog(undefined), []);

  const onPress = useCallback(
    (name: string) => {
      if (name === 'onboard') {
        history.push(
          getPathWithUrlBase(`/members/add?pick=${member.discordId}`)
        );
      } else if (DIALOGS[name]) {
        setDialog(DIALOGS[name]);
      } else {
        action.act(`${path}/${name}`);
      }
    },
    [action, history, member.discordId, path]
  );
  const onRemove = useCallback(() => {
    setDialog(undefined);
    action.act(`${path}/remove`);
  }, [action, path]);
  const onPlex = useCallback(
    (username: string) => {
      setDialog(undefined);

      // Enter on the window confirms too: an empty name isn't sent.
      if (username.trim()) {
        action.act(`${path}/plex`, { username });
      }
    },
    [action, path]
  );

  const buttons = actionsFor(detail).map((name) => (
    <PageToolbarButton
      key={name}
      label={actionLabel(name)}
      iconName={ICONS[name]}
      isDisabled={action.isBusy}
      // eslint-disable-next-line react/jsx-no-bind -- a toolbar button per action, each pressing its own name
      onPress={() => onPress(name)}
    />
  ));

  const dialogs = (
    <>
      <ConfirmModal
        isOpen={dialog === 'remove'}
        kind={kinds.DANGER}
        title={translate('PortalActionRemoveMember')}
        message={translate('PortalRemoveMemberMessage', { name: member.name })}
        confirmLabel={translate('PortalActionRemoveMember')}
        onConfirm={onRemove}
        onCancel={onClose}
      />
      <TextConfirmModal
        prompt={dialog === 'plex' ? plexPrompt() : undefined}
        onConfirm={onPlex}
        onClose={onClose}
      />
      <EditAccessModal
        detail={detail}
        action={action}
        isOpen={dialog === 'access'}
        onModalClose={onClose}
      />
    </>
  );

  return { buttons, dialogs };
}

export default useMemberDetailActions;
