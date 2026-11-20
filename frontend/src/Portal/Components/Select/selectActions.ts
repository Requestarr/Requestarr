import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import { useMemo } from 'react';
import { useSelect } from 'App/SelectContext';
import { icons } from 'Helpers/Props';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import { actionLabel } from '../../Requests/ActionButton';

// An action on what's picked, as a toolbar button left of the page's own buttons (the owner's choice over Servarr's
// bar along the bottom: docs/development.md, "Deviations from Servarr").
export interface ToolbarAction {
  name: string;
  label: string;
  iconName: IconDefinition;
  isDisabled: boolean;
  onPress: () => void;
}

const ICONS: Record<string, IconDefinition> = {
  approve: icons.CHECK,
  deny: icons.CLOSE,
  available: icons.CHECK_CIRCLE,
  cancel: icons.REMOVE,
  searchAgain: icons.SEARCH,
  retry: icons.REFRESH,
  addSeasons: icons.ADD,
  remove: icons.DELETE,
  forceRemoval: icons.DANGER,
  dropRemoval: icons.RESTORE,
  deletedByHand: icons.CHECK_SQUARE,
  deleteRecord: icons.DELETE,
  onboard: icons.ADD,
  suspend: icons.PAUSED,
  unsuspend: icons.RESTORE,
  changeAccess: icons.EDIT,
  'remove-member': icons.DELETE,
};

// The items picked in the page's select state, in the list's order.
export function usePicked<T extends { id: number }>(items: T[]) {
  const [selectState] = useSelect();
  const { selectedState } = selectState;

  return useMemo(() => {
    const ids = getSelectedIds(selectedState);

    return items.filter((item) => ids.includes(item.id));
  }, [items, selectedState]);
}

// The group's actions, always shown and greyed out unless every pick allows them; with one pick, also everything else
// that item's own page offers.
export function toolbarActions<T>(
  group: string[],
  picked: T[],
  actionsOf: (item: T) => string[],
  onAction: (name: string) => void
): ToolbarAction[] {
  const own = picked.length === 1 ? actionsOf(picked[0]) : [];
  const names = [...group, ...own.filter((name) => !group.includes(name))];

  return names.map((name) => ({
    name,
    label: actionLabel(name),
    iconName: ICONS[name] ?? icons.ACTIONS,
    isDisabled:
      picked.length === 0 ||
      !picked.every((item) => actionsOf(item).includes(name)),
    onPress: () => onAction(name),
  }));
}
