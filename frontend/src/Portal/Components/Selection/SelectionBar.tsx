import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import { kinds, sizes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { Selection } from './useSelection';
import styles from './SelectionBar.css';

// One of the bar's actions: disabled, with why on hover, when not every pick offers it.
export interface BarAction {
  name: string;
  label: string;
  isDanger: boolean;
  whyNot: string | null;
}

interface BarButtonProps {
  action: BarAction;
  isBusy: boolean;
  onPress: (name: string) => void;
}

function BarButton({ action, isBusy, onPress }: BarButtonProps) {
  const onButtonPress = useCallback(
    () => onPress(action.name),
    [action.name, onPress]
  );

  return (
    <span title={action.whyNot ?? undefined}>
      <Button
        kind={action.isDanger ? kinds.DANGER : kinds.PRIMARY}
        size={sizes.SMALL}
        isDisabled={isBusy || action.whyNot !== null}
        onPress={onButtonPress}
      >
        {action.label}
      </Button>
    </span>
  );
}

// Once the page is all picked: picking everything matching the filters, or that it's done.
function AllMatching({ selection }: { selection: Selection }) {
  const { picked, matchingCount, allOnPage, onSelectAllMatching } = selection;

  if (picked.length === matchingCount) {
    return (
      <span>
        {translate('PortalAllMatchingSelected', { count: matchingCount })}
      </span>
    );
  }

  if (!allOnPage) {
    return null;
  }

  return (
    <Link onPress={onSelectAllMatching}>
      {translate('PortalSelectAllMatching', { count: matchingCount })}
    </Link>
  );
}

interface SelectionBarProps {
  selection: Selection;
  actions: BarAction[];
  isBusy: boolean;
  onAction: (name: string) => void;
}

// Shown as soon as anything is picked, stuck to the top of the list while it scrolls: how many, what every one of
// them allows now, picking all that match, and Clear selection.
function SelectionBar({
  selection,
  actions,
  isBusy,
  onAction,
}: SelectionBarProps) {
  if (!selection.picked.length) {
    return null;
  }

  return (
    <div className={styles.bar}>
      <span className={styles.count}>
        {translate('PortalSelectedCount', { count: selection.picked.length })}
      </span>
      <span className={styles.actions}>
        {actions.map((action) => (
          <BarButton
            key={action.name}
            action={action}
            isBusy={isBusy}
            onPress={onAction}
          />
        ))}
      </span>
      <AllMatching selection={selection} />
      <Link className={styles.clear} onPress={selection.onClear}>
        {translate('PortalClearSelection')}
      </Link>
    </div>
  );
}

export default SelectionBar;
