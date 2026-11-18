import React from 'react';
import translate from 'Utilities/String/translate';
import BotStatus from './BotStatus';
import RequestarrSettingsPage from './RequestarrSettingsPage';
import { SetupStatus } from './RequestarrSettingsTypes';
import { DISCORD_SECTIONS } from './settingsPages';
import useRequestarrApi from './useRequestarrApi';

function DiscordSettings() {
  const { data: setup } = useRequestarrApi<SetupStatus>('setup');

  return (
    <RequestarrSettingsPage
      title={translate('SettingsDiscord')}
      sections={DISCORD_SECTIONS}
    >
      {setup && <BotStatus setup={setup} />}
    </RequestarrSettingsPage>
  );
}

export default DiscordSettings;
