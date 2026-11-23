import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import SpinnerErrorButton from 'Components/Link/SpinnerErrorButton';
import ModalFooter from 'Components/Modal/ModalFooter';
import { kinds } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { toggleAdvancedSettings } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import styles from './ConnectionModalFooter.css';

interface ConnectionModalFooterProps {
  isReadOnly: boolean;
  isSaving: boolean;
  isTesting?: boolean;
  // What the last test or save failed with, as Servarr's provider store keeps it: Test's mark shows it.
  testError?: object | null;
  canSave: boolean;
  // The modal has fields shown only with Show Advanced on.
  hasAdvanced?: boolean;
  onDelete?: () => void;
  onTest?: () => void;
  onCancel: () => void;
  onSave: () => void;
}

// Show Advanced, the same switch as the settings pages', kept across pages.
function AdvancedButton() {
  const dispatch = useDispatch();
  const advancedSettings = useSelector(
    (state: AppState) => state.settings.advancedSettings
  );
  const onPress = useCallback(
    () => dispatch(toggleAdvancedSettings()),
    [dispatch]
  );

  return (
    <AdvancedSettingsButton
      advancedSettings={advancedSettings}
      showLabel={false}
      onAdvancedSettingsPress={onPress}
    />
  );
}

// Every connection's edit modal ends as Prowlarr's EditApplicationModalContent does: Delete on the left where a saved
// connection can be deleted, then Show Advanced where there are advanced fields, Test (a spinner while it runs, then a
// green check or a red mark), Cancel and Save.
function ConnectionModalFooter(props: ConnectionModalFooterProps) {
  const { isReadOnly, isSaving, isTesting = false, onDelete, onTest } = props;

  return (
    <ModalFooter>
      {onDelete && (
        <Button
          className={styles.deleteButton}
          kind={kinds.DANGER}
          isDisabled={isReadOnly}
          onPress={onDelete}
        >
          {translate('Delete')}
        </Button>
      )}

      {props.hasAdvanced && <AdvancedButton />}

      {onTest && (
        <SpinnerErrorButton
          isSpinning={isTesting}
          isDisabled={isReadOnly || isSaving}
          error={props.testError}
          onPress={onTest}
        >
          {translate('Test')}
        </SpinnerErrorButton>
      )}

      <Button onPress={props.onCancel}>{translate('Cancel')}</Button>

      <SpinnerButton
        kind={kinds.PRIMARY}
        isSpinning={isSaving}
        isDisabled={isReadOnly || !props.canSave || isTesting}
        onPress={props.onSave}
      >
        {translate('Save')}
      </SpinnerButton>
    </ModalFooter>
  );
}

export default ConnectionModalFooter;
