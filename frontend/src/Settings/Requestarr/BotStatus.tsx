import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import { kinds } from 'Helpers/Props';
import { Kind } from 'Helpers/Props/kinds';
import Person from 'Portal/Components/Person';
import translate from 'Utilities/String/translate';
import { BotState, SetupStatus } from './RequestarrSettingsTypes';

const STATE_KINDS: Record<BotState, Kind> = {
  notConfigured: kinds.WARNING,
  connecting: kinds.INFO,
  connected: kinds.SUCCESS,
  disconnected: kinds.WARNING,
  failed: kinds.DANGER,
};

// Whether the bot is connected, why not, and the bot as Discord shows it (its picture and name), which confirms the
// token is the right one. Adding it to a server is the servers' "+" card.
function BotStatus({ setup }: { setup: SetupStatus }) {
  const { botState, bot } = setup;
  const state = `BotState${botState[0].toUpperCase()}${botState.slice(1)}`;

  return (
    <FieldSet legend={translate('SettingsBotStatus')}>
      {bot && <Person name={bot.name} avatar={bot.avatarUrl} size="large" />}

      <Alert kind={STATE_KINDS[botState]}>
        {bot && botState === 'connected'
          ? translate('SetupBotConnectedAs', { name: bot.name })
          : translate(state)}
        {setup.botProblem ? `: ${setup.botProblem}` : ''}
      </Alert>
    </FieldSet>
  );
}

export default BotStatus;
