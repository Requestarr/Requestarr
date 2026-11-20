import React, { useCallback } from 'react';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import PageCheckbox from './PageCheckbox';
import { Selection } from './useSelection';
import styles from './SelectionBar.css';

// One of the bar's actions: shown only when every pick offers it now. Destructive ones (a removal, deleting a record)
// are red; the rest look like the app's other buttons.
export interface BarAction {
  name: string;
  label: string;
  isDanger: boolean;
  isOffered: boolean;
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
    <Button
      kind={action.isDanger ? kinds.DANGER : kinds.DEFAULT}
      isDisabled={isBusy}
      onPress={onButtonPress}
    >
      {action.label}
    </Button>
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
  // What the row says while nothing is picked ("Showing 464 of 464 requests").
  summary: string;
  // A box for the page, above a list without a table header to hold one (Overview, Posters).
  withPageBox?: boolean;
  onAction: (name: string) => void;
}

// The row at the top of a list, stuck there while it scrolls, the same height whatever is picked so nothing below it
// moves: how many there are, or once something is picked, how many, what every one of them allows now, picking all
// that match, and Clear selection.
function SelectionBar(props: SelectionBarProps) {
  const { selection, actions, isBusy, summary, withPageBox, onAction } = props;
  const count = selection.picked.length;

  return (
    <div className={styles.bar}>
      {withPageBox && <PageCheckbox selection={selection} />}
      {count === 0 && <span className={styles.summary}>{summary}</span>}
      {count > 0 && (
        <>
          <span className={styles.count}>
            {translate('PortalSelectedCount', { count })}
          </span>
          <span className={styles.actions}>
            {actions
              .filter((action) => action.isOffered)
              .map((action) => (
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
        </>
      )}
    </div>
  );
}

export default SelectionBar;
