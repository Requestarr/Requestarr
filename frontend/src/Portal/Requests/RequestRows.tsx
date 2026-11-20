import React, { createContext, useCallback, useContext } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import Button from 'Components/Link/Button';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import { align, icons, kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { Selection } from '../Components/Selection/useSelection';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { actionLabel } from './ActionButton';
import styles from './RequestRows.css';

// What the Requests page lets its rows do: each row's actions, and picking rows. A list of requests elsewhere (a
// member's page) has none of it.
// isBusy: an action is under way, so the rows' own buttons wait for it.
export interface RequestRows {
  onAction: (request: PortalRequest, action: string) => void;
  isBusy: boolean;
  selection: Selection;
}

export const RequestRowsContext = createContext<RequestRows | null>(null);

// The action a row shows as its own button, the one most likely wanted: Approve while it waits, Start removal once
// it's there, Delete record for history. Everything is also in the menu.
const LIKELY: [string, typeof icons.CHECK][] = [
  ['approve', icons.CHECK],
  ['remove', icons.DELETE],
  ['deleteRecord', icons.DELETE],
];

interface RowActionProps {
  request: PortalRequest;
  action: string;
  onAction: RequestRows['onAction'];
}

function RowActionItem({
  request,
  action,
  isBusy,
  onAction,
}: RowActionProps & { isBusy: boolean }) {
  const onPress = useCallback(
    () => onAction(request, action),
    [request, action, onAction]
  );

  return (
    <MenuItem isDisabled={isBusy} onPress={onPress}>
      {actionLabel(action)}
    </MenuItem>
  );
}

interface LikelyButtonProps extends RowActionProps {
  icon: typeof icons.CHECK;
  isBusy: boolean;
}

function LikelyButton(props: LikelyButtonProps) {
  const { request, action, icon, isBusy, onAction } = props;
  const onPress = useCallback(
    () => onAction(request, action),
    [request, action, onAction]
  );

  return (
    <Button
      kind={action === 'approve' ? kinds.SUCCESS : kinds.DANGER}
      size={sizes.SMALL}
      isDisabled={isBusy}
      onPress={onPress}
    >
      <Icon name={icon} /> {actionLabel(action)}
    </Button>
  );
}

// A row's actions beside its title and status: its likely action as a button, then a menu of everything its page
// offers now and Open in Sonarr or Radarr.
export function RowActions({ request }: { request: PortalRequest }) {
  const rows = useContext(RequestRowsContext);
  const { media } = request;
  const likely = LIKELY.find(([name]) => request.actions.includes(name));

  if (!rows || (!request.actions.length && !media?.openUrl)) {
    return null;
  }

  return (
    <span className={styles.actions}>
      {likely && (
        <LikelyButton
          request={request}
          action={likely[0]}
          icon={likely[1]}
          isBusy={rows.isBusy}
          onAction={rows.onAction}
        />
      )}
      <Menu alignMenu={align.LEFT}>
        <MenuButton
          className={styles.button}
          title={translate('PortalActions')}
          aria-label={translate('PortalActionsFor', {
            title: titleText(request),
          })}
        >
          <Icon name={icons.OVERFLOW} />
        </MenuButton>
        <MenuContent>
          {request.actions.map((action) => (
            <RowActionItem
              key={action}
              request={request}
              action={action}
              isBusy={rows.isBusy}
              onAction={rows.onAction}
            />
          ))}
          {media?.openUrl && media.openIn && (
            <MenuItem to={media.openUrl}>
              {translate('PortalOpenIn', { app: media.openIn })}
            </MenuItem>
          )}
        </MenuContent>
      </Menu>
    </span>
  );
}

// A row's checkbox, always there, as in an email list.
export function RowSelect({ request }: { request: PortalRequest }) {
  const rows = useContext(RequestRowsContext);
  const onSelect = rows?.selection.onSelect;
  const onChange = useCallback(
    ({ value }: { value: boolean }) => onSelect?.(request.id, value),
    [onSelect, request.id]
  );

  if (!rows) {
    return null;
  }

  return (
    <CheckInput
      containerClassName={styles.select}
      name={`select-${request.id}`}
      value={rows.selection.selected.has(request.id)}
      onChange={onChange}
    />
  );
}
