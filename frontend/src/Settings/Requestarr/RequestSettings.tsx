import React from 'react';
import translate from 'Utilities/String/translate';
import RequestarrSettingsPage from './RequestarrSettingsPage';
import { REQUEST_SECTIONS } from './settingsPages';

function RequestSettings() {
  return (
    <RequestarrSettingsPage
      title={translate('SettingsRequests')}
      sections={REQUEST_SECTIONS}
    />
  );
}

export default RequestSettings;
