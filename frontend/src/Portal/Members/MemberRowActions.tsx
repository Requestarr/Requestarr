import React, { useCallback } from 'react';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import { align, icons, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { PortalMember } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import { likelyMemberAction, memberActions } from './memberActionRules';
import styles from '../Requests/RequestRows.css';

interface MemberActionProps {
  member: PortalMember;
  action: string;
  isBusy: boolean;
  onAction: (members: PortalMember[], action: string) => void;
}

function MemberActionItem({
  member,
  action,
  isBusy,
  onAction,
}: MemberActionProps) {
  const onPress = useCallback(
    () => onAction([member], action),
    [member, action, onAction]
  );

  return (
    <MenuItem isDisabled={isBusy} onPress={onPress}>
      {actionLabel(action)}
    </MenuItem>
  );
}

function LikelyMemberButton({
  member,
  action,
  isBusy,
  onAction,
}: MemberActionProps) {
  const onPress = useCallback(
    () => onAction([member], action),
    [member, action, onAction]
  );

  return (
    <Button
      kind={action === 'onboard' ? kinds.SUCCESS : kinds.DEFAULT}
      size={sizes.SMALL}
      isDisabled={isBusy}
      onPress={onPress}
    >
      {actionLabel(action)}
    </Button>
  );
}

// A member's actions beside their name and status: the likely one as a button, then a menu of everything.
function MemberRowActions(props: Omit<MemberActionProps, 'action'>) {
  const { member } = props;
  const actions = memberActions(member);
  const likely = likelyMemberAction(member);

  if (!actions.length) {
    return null;
  }

  return (
    <span className={styles.actions}>
      {likely && <LikelyMemberButton {...props} action={likely} />}
      <Menu alignMenu={align.LEFT}>
        <MenuButton
          className={styles.button}
          title={translate('PortalActions')}
          aria-label={translate('PortalActionsFor', { title: member.name })}
        >
          <Icon name={icons.OVERFLOW} />
        </MenuButton>
        <MenuContent>
          {actions.map((action) => (
            <MemberActionItem key={action} {...props} action={action} />
          ))}
        </MenuContent>
      </Menu>
    </span>
  );
}

export default MemberRowActions;
