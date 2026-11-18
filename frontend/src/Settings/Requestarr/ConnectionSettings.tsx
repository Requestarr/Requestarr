import React from 'react';
import translate from 'Utilities/String/translate';
import RequestarrSettingsPage from './RequestarrSettingsPage';
import { CONNECTION_SECTIONS } from './settingsPages';

function ConnectionSettings() {
  return (
    <RequestarrSettingsPage
      title={translate('SettingsConnections')}
      sections={CONNECTION_SECTIONS}
    />
  );
}

export default ConnectionSettings;
