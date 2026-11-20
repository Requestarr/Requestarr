import React, { useCallback } from 'react';
import SpinnerButton from 'Components/Link/SpinnerButton';
import PageContentFooter from 'Components/Page/PageContentFooter';
import { kinds } from 'Helpers/Props';
import styles from './SelectFooter.css';

// One of the bar's actions. Only actions every pick offers now are shown; destructive ones (a removal, deleting a
// record) sit apart on the right in red, as Prowlarr's Delete does.
export interface SelectAction {
  name: string;
  label: string;
  isDanger: boolean;
  isOffered: boolean;
}

interface ActionButtonProps {
  action: SelectAction;
  isSpinning: boolean;
  onPress: (name: string) => void;
}

function ActionButton({ action, isSpinning, onPress }: ActionButtonProps) {
  const onButtonPress = useCallback(
    () => onPress(action.name),
    [action.name, onPress]
  );

  return (
    <SpinnerButton
      kind={action.isDanger ? kinds.DANGER : kinds.DEFAULT}
      isSpinning={isSpinning}
      isDisabled={isSpinning}
      onPress={onButtonPress}
    >
      {action.label}
    </SpinnerButton>
  );
}

interface SelectFooterProps {
  actions: SelectAction[];
  selectedText: string;
  isBusy: boolean;
  onAction: (name: string) => void;
}

// The bar along the bottom in select mode, as Prowlarr's IndexerIndexSelectFooter: the actions for what's picked,
// and how many are picked.
function SelectFooter({
  actions,
  selectedText,
  isBusy,
  onAction,
}: SelectFooterProps) {
  const offered = actions.filter((action) => action.isOffered);
  const button = (action: SelectAction) => (
    <ActionButton
      key={action.name}
      action={action}
      isSpinning={isBusy}
      onPress={onAction}
    />
  );

  return (
    <PageContentFooter className={styles.footer}>
      <div className={styles.buttons}>
        <div className={styles.actionButtons}>
          {offered.filter((action) => !action.isDanger).map(button)}
        </div>

        <div className={styles.deleteButtons}>
          {offered.filter((action) => action.isDanger).map(button)}
        </div>
      </div>

      <div className={styles.selected}>{selectedText}</div>
    </PageContentFooter>
  );
}

export default SelectFooter;
