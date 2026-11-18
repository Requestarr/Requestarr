import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import { kinds } from 'Helpers/Props';
import { Kind } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';
import { BotState, SetupStatus } from './RequestarrSettingsTypes';

const STATE_KINDS: Record<BotState, Kind> = {
  notConfigured: kinds.WARNING,
  connecting: kinds.INFO,
  connected: kinds.SUCCESS,
  disconnected: kinds.WARNING,
  failed: kinds.DANGER,
};

interface BotStatusProps {
  setup: SetupStatus;
}

// Whether the bot is connected, why not, and the link that adds it to a Discord server.
function BotStatus({ setup }: BotStatusProps) {
  const { botState } = setup;
  const state = `BotState${botState[0].toUpperCase()}${botState.slice(1)}`;

  return (
    <FieldSet legend={translate('SettingsBotStatus')}>
      <Alert kind={STATE_KINDS[setup.botState]}>
        {translate(state)}
        {setup.botProblem ? `: ${setup.botProblem}` : ''}
      </Alert>

      {setup.inviteUrl && (
        <Link to={setup.inviteUrl}>{translate('SettingsBotInvite')}</Link>
      )}
    </FieldSet>
  );
}

export default BotStatus;
