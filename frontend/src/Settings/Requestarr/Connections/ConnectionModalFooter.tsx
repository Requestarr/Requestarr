import React from 'react';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import ModalFooter from 'Components/Modal/ModalFooter';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface ConnectionModalFooterProps {
  isReadOnly: boolean;
  isBusy: boolean;
  canSave: boolean;
  onTest?: () => void;
  onCancel: () => void;
  onSave: () => void;
}

// Every connection's edit modal ends the same way, as Sonarr's and Radarr's do: Test where the connection can be
// tested, Cancel and Save.
function ConnectionModalFooter({
  isReadOnly,
  isBusy,
  canSave,
  onTest,
  onCancel,
  onSave,
}: ConnectionModalFooterProps) {
  return (
    <ModalFooter>
      {onTest && (
        <SpinnerButton
          isSpinning={isBusy}
          isDisabled={isReadOnly}
          onPress={onTest}
        >
          {translate('Test')}
        </SpinnerButton>
      )}

      <Button onPress={onCancel}>{translate('Cancel')}</Button>

      <SpinnerButton
        kind={kinds.PRIMARY}
        isSpinning={isBusy}
        isDisabled={isReadOnly || !canSave}
        onPress={onSave}
      >
        {translate('Save')}
      </SpinnerButton>
    </ModalFooter>
  );
}

export default ConnectionModalFooter;
