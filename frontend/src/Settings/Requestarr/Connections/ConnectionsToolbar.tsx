import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { icons } from 'Helpers/Props';
import { testAllMediaManagers } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import SettingsToolbar from '../settingsToolbar';
import { SettingsForm } from '../useSettingsForm';
import styles from './ConnectionsToolbar.css';

// Show Advanced, the same switch as every settings page's, kept across pages.
export function useShowAdvanced() {
  return useSelector((state: AppState) => state.settings.advancedSettings);
}

interface ConnectionsToolbarProps {
  options: SettingsForm;
}

// Servarr's settings toolbar (Save for the Options below the cards, and the prompt when leaving with changes unsaved),
// with Test All Connections after it, as Prowlarr's Applications settings add Test All Apps. Each card saves from its
// own modal, whose footer has Show Advanced; the Options have no advanced fields, so the page has none.
function ConnectionsToolbar({ options }: ConnectionsToolbarProps) {
  const dispatch = useDispatch();
  const isTestingAll = useSelector(
    (state: AppState) => state.settings.mediaManagers.isTestingAll
  );
  const { hasChanges, isSaving, onSave } = options;

  const onTestAllPress = useCallback(
    () => dispatch(testAllMediaManagers()),
    [dispatch]
  );

  return (
    <SettingsToolbar
      hasPendingChanges={hasChanges}
      isSaving={isSaving}
      showAdvancedSettings={false}
      additionalButtons={
        <>
          <PageToolbarSeparator />

          <PageToolbarButton
            className={styles.testAll}
            label={translate('TestAllConnections')}
            iconName={icons.TEST}
            isSpinning={isTestingAll}
            onPress={onTestAllPress}
          />
        </>
      }
      onSavePress={onSave}
    />
  );
}

export default ConnectionsToolbar;
