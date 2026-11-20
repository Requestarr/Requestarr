import React, { createContext, useCallback, useContext } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import IconButton from 'Components/Link/IconButton';
import Menu from 'Components/Menu/Menu';
import MenuButton from 'Components/Menu/MenuButton';
import MenuContent from 'Components/Menu/MenuContent';
import MenuItem from 'Components/Menu/MenuItem';
import { align, icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { actionLabel } from './ActionButton';
import styles from './RequestRows.css';

// What the Requests page lets its rows do: each row's actions, and picking rows in select mode. A list of requests
// elsewhere (a member's page) has none of it.
export interface RequestRows {
  onAction: (request: PortalRequest, action: string) => void;
  isSelecting: boolean;
  selected: ReadonlySet<number>;
  onSelect: (id: number, isSelected: boolean) => void;
}

export const RequestRowsContext = createContext<RequestRows | null>(null);

interface RowActionItemProps {
  request: PortalRequest;
  action: string;
  onAction: RequestRows['onAction'];
}

function RowActionItem({ request, action, onAction }: RowActionItemProps) {
  const onPress = useCallback(
    () => onAction(request, action),
    [request, action, onAction]
  );

  return <MenuItem onPress={onPress}>{actionLabel(action)}</MenuItem>;
}

// The row's trash button, as Sonarr's rows have one: Start removal for a downloaded title, Delete record for history.
// It asks first, as from the menu.
const TRASH_ACTIONS = ['remove', 'deleteRecord'];

function RowTrash({ request, onAction }: Omit<RowActionItemProps, 'action'>) {
  const action = request.actions.find((name) => TRASH_ACTIONS.includes(name));
  const onPress = useCallback(
    () => action && onAction(request, action),
    [request, action, onAction]
  );

  if (!action) {
    return null;
  }

  return (
    <IconButton
      name={icons.DELETE}
      title={actionLabel(action)}
      onPress={onPress}
    />
  );
}

// A row's actions: its trash button, then a menu of what the request's page offers it now and Open in Sonarr or
// Radarr.
export function RowActions({ request }: { request: PortalRequest }) {
  const rows = useContext(RequestRowsContext);
  const { media } = request;

  if (!rows || (!request.actions.length && !media?.openUrl)) {
    return null;
  }

  return (
    <span className={styles.actions}>
      <RowTrash request={request} onAction={rows.onAction} />
      <Menu alignMenu={align.RIGHT}>
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

// A row's checkbox in select mode.
export function RowSelect({ request }: { request: PortalRequest }) {
  const rows = useContext(RequestRowsContext);
  const onSelect = rows?.onSelect;
  const onChange = useCallback(
    ({ value }: { value: boolean }) => onSelect?.(request.id, value),
    [onSelect, request.id]
  );

  if (!rows?.isSelecting) {
    return null;
  }

  return (
    <CheckInput
      containerClassName={styles.select}
      name={`select-${request.id}`}
      value={rows.selected.has(request.id)}
      onChange={onChange}
    />
  );
}
