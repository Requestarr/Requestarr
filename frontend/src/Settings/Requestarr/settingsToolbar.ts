import { ComponentType, ReactNode } from 'react';
import SettingsToolbarConnector from 'Settings/SettingsToolbarConnector';

interface SettingsToolbarProps {
  hasPendingChanges: boolean;
  isSaving: boolean;
  onSavePress: () => void;
  additionalButtons?: ReactNode;
  showAdvancedSettings?: boolean;
}

// Servarr's settings toolbar (Save Changes, Show Advanced, and the prompt when leaving with changes unsaved), typed for
// Requestarr's TypeScript settings pages: the connector itself is JavaScript.
const settingsToolbar =
  SettingsToolbarConnector as unknown as ComponentType<SettingsToolbarProps>;

export default settingsToolbar;
