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

// An action's words, by its name in the portal's answer: PortalAction and the name ("forceRemoval": Force removal).
export function actionLabel(action: string) {
  const name = action.replace(/(^|-)(\w)/g, (_, __, letter: string) =>
    letter.toUpperCase()
  );

  return translate(`PortalAction${name}`);
}

// A dialog's heading: the action, and the title it acts on when given ("Deny: Dune (2021)").
export function actionHeading(action: string, title?: string) {
  return title
    ? translate('PortalActionOn', { action: actionLabel(action), title })
    : actionLabel(action);
}

interface ActionButtonProps {
  action: string;
  isBusy: boolean;
  isDisabled?: boolean;
  onPress: (action: string) => void;
}

// One of an item's actions, red when it's destructive.
function ActionButton({
  action,
  isBusy,
  isDisabled,
  onPress,
}: ActionButtonProps) {
  const onButtonPress = useCallback(() => onPress(action), [action, onPress]);
  const kind: Kind = DANGER.includes(action) ? kinds.DANGER : kinds.DEFAULT;

  return (
    <SpinnerButton
      kind={kind}
      isSpinning={isBusy}
      isDisabled={isDisabled}
      onPress={onButtonPress}
    >
      {actionLabel(action)}
    </SpinnerButton>
  );
}

export default ActionButton;
