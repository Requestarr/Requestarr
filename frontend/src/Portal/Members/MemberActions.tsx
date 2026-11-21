import React, { useCallback, useState } from 'react';
import { useHistory } from 'react-router-dom';
import FieldSet from 'Components/FieldSet';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import TextConfirmModal from '../Components/TextConfirmModal';
import { isImported } from '../portalText';
import {
  ContentType,
  PortalInstance,
  PortalMemberDetail,
} from '../PortalTypes';
import ActionButton from '../Requests/ActionButton';
import usePortal from '../usePortal';
import usePortalAction, { PortalAction } from '../usePortalAction';
import AccessRow from './AccessRow';
import styles from '../Portal.css';

function plexPrompt() {
  return {
    title: translate('PortalActionLinkPlex'),
    label: translate('PortalPlexUsername'),
    helpText: translate('PortalPlexUsernameHelpText'),
    confirmLabel: translate('PortalActionLinkPlex'),
  };
}

interface StatusActionsProps {
  member: PortalMemberDetail['member'];
  action: PortalAction;
}

// Suspend or unsuspend, remove (after asking), and link a Plex account (asking its username).
function StatusActions({ member, action }: StatusActionsProps) {
  const [dialog, setDialog] = useState<'remove' | 'plex'>();
  const path = `members/${member.id}`;
  const status = member.status === 'suspended' ? 'unsuspend' : 'suspend';

  const onClose = useCallback(() => setDialog(undefined), []);
  const onPress = useCallback(
    (name: string) => {
      if (name === 'remove-member') {
        setDialog('remove');
      } else if (name === 'link-plex') {
        setDialog('plex');
      } else {
        action.act(`${path}/${name}`);
      }
    },
    [action, path]
  );
  const onRemove = useCallback(() => {
    setDialog(undefined);
    action.act(`${path}/remove`);
  }, [action, path]);
  const onPlex = useCallback(
    (username: string) => {
      setDialog(undefined);
      action.act(`${path}/plex`, { username });
    },
    [action, path]
  );

  return (
    <>
      <div className={styles.actions}>
        {[status, 'remove-member', 'link-plex'].map((name) => (
          <ActionButton
            key={name}
            action={name}
            isBusy={action.isBusy}
            onPress={onPress}
          />
        ))}
      </div>

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
    </>
  );
}

// Someone known only from an import: onboarding them is Add member's setup, with them picked; their imported requests
// stay theirs.
function OnboardAction({ member, action }: StatusActionsProps) {
  const history = useHistory();
  const onPress = useCallback(
    () =>
      history.push(getPathWithUrlBase(`/members/add?pick=${member.discordId}`)),
    [history, member]
  );

  return (
    <div className={styles.actions}>
      <ActionButton action="onboard" isBusy={action.isBusy} onPress={onPress} />
    </div>
  );
}

interface MemberActionsProps {
  detail: PortalMemberDetail;
  onDone: () => void;
}

// What the member card in Discord can do: access, instance and limit per type, suspend or unsuspend, remove, and
// link a Plex account.
function MemberActions({ detail, onDone }: MemberActionsProps) {
  const action = usePortalAction(onDone);
  const { data: instances } = usePortal<PortalInstance[]>('instances');
  const { member } = detail;
  const { data: offered } = usePortal<ContentType[]>('types');
  const types = offered ?? [];

  if (member.status === 'removed') {
    return null;
  }

  if (isImported(member.status)) {
    return (
      <FieldSet legend={translate('PortalActions')}>
        <OnboardAction member={member} action={action} />
      </FieldSet>
    );
  }

  return (
    <FieldSet legend={translate('PortalActions')}>
      <StatusActions member={member} action={action} />

      {types.map((type) => (
        <AccessRow
          key={type}
          memberId={member.id}
          type={type}
          access={member.access.find((a) => a.contentType === type)}
          instances={(instances ?? []).filter((i) => i.contentType === type)}
          action={action}
        />
      ))}
    </FieldSet>
  );
}

export default MemberActions;
