import React, { useCallback } from 'react';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import { Kind } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';

// Destructive actions look it, as Discord's red buttons do.
const DANGER = [
  'deny',
  'cancel',
  'remove',
  'forceRemoval',
  'suspend',
  'remove-member',
];

interface ActionButtonProps {
  action: string;
  isBusy: boolean;
  onPress: (action: string) => void;
}

// One of an item's actions, by its name in the portal's answer; its words are PortalAction and the name.
function ActionButton({ action, isBusy, onPress }: ActionButtonProps) {
  const onButtonPress = useCallback(() => onPress(action), [action, onPress]);
  const kind: Kind = DANGER.includes(action) ? kinds.DANGER : kinds.DEFAULT;
  const name = action.replace(/(^|-)(\w)/g, (_, __, letter: string) =>
    letter.toUpperCase()
  );

  return (
    <SpinnerButton kind={kind} isSpinning={isBusy} onPress={onButtonPress}>
      {translate(`PortalAction${name}`)}
    </SpinnerButton>
  );
}

export default ActionButton;
