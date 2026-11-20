import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import { icons } from 'Helpers/Props';
import AdvancedSettingsButton from 'Settings/AdvancedSettingsButton';
import { toggleAdvancedSettings } from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import { SettingsForm } from '../useSettingsForm';
import styles from './ConnectionsToolbar.css';

// Show Advanced, the same switch as every settings page's, kept across pages.
export function useShowAdvanced() {
  return useSelector((state: AppState) => state.settings.advancedSettings);
}

interface ConnectionsToolbarProps {
  options: SettingsForm;
  isTesting: boolean;
  onTestAll: () => void;
}

// The toolbar of Sonarr's Indexers and Download Clients settings: Show Advanced, Save (for the Options below the
// cards), then Test All Connections. Each card saves from its own modal.
function ConnectionsToolbar({
  options,
  isTesting,
  onTestAll,
}: ConnectionsToolbarProps) {
  const dispatch = useDispatch();
  const showAdvanced = useShowAdvanced();
  const { hasChanges, isSaving, onSave } = options;

  const onAdvancedPress = useCallback(
    () => dispatch(toggleAdvancedSettings()),
    [dispatch]
  );

  return (
    <PageToolbar>
      <PageToolbarSection>
        <AdvancedSettingsButton
          advancedSettings={showAdvanced}
          onAdvancedSettingsPress={onAdvancedPress}
        />

        <PageToolbarButton
          label={hasChanges ? translate('SaveChanges') : translate('NoChanges')}
          iconName={icons.SAVE}
          isSpinning={isSaving}
          isDisabled={!hasChanges}
          onPress={onSave}
        />

        <PageToolbarSeparator />

        <PageToolbarButton
          className={styles.testAll}
          label={translate('TestAllConnections')}
          iconName={icons.TEST}
          isSpinning={isTesting}
          onPress={onTestAll}
        />
      </PageToolbarSection>
    </PageToolbar>
  );
}

export default ConnectionsToolbar;
