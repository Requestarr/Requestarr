import React from 'react';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { ToolbarAction } from './selectActions';

interface SelectActionButtonsOptions {
  actions: ToolbarAction[];
  isSelectMode: boolean;
  isBusy: boolean;
}

// In select mode, a toolbar button for each action on the picks, then a separator. A function, not a component:
// PageToolbarSection measures its buttons and folds them into its overflow menu by their own props, so they must be
// its direct children.
function selectActionButtons({
  actions,
  isSelectMode,
  isBusy,
}: SelectActionButtonsOptions) {
  if (!isSelectMode) {
    return [];
  }

  return [
    ...actions.map((action) => (
      <PageToolbarButton
        key={action.name}
        label={action.label}
        iconName={action.iconName}
        isDisabled={action.isDisabled || isBusy}
        onPress={action.onPress}
      />
    )),
    <PageToolbarSeparator key="selectActionsEnd" />,
  ];
}

export default selectActionButtons;
