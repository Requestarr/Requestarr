import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import React, { useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';

interface SelectModeButtonProps {
  label: string;
  iconName: IconDefinition;
  isSelectMode: boolean;
  overflowComponent: React.FunctionComponent<never>;
  onPress: () => void;
}

function SelectModeButton(props: SelectModeButtonProps) {
  const { label, iconName, isSelectMode, onPress } = props;
  const [, selectDispatch] = useSelect();

  const onPressWrapper = useCallback(() => {
    if (isSelectMode) {
      selectDispatch({
        type: 'reset',
      });
    }

    onPress();
  }, [isSelectMode, onPress, selectDispatch]);

  return (
    <PageToolbarButton
      label={label}
      iconName={iconName}
      onPress={onPressWrapper}
    />
  );
}

export default SelectModeButton;
