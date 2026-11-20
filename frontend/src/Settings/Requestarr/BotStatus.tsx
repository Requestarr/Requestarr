import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import { kinds } from 'Helpers/Props';
import { Kind } from 'Helpers/Props/kinds';
import translate from 'Utilities/String/translate';
import { BotState, SetupStatus } from './RequestarrSettingsTypes';
import styles from './BotStatus.css';

const STATE_KINDS: Record<BotState, Kind> = {
  notConfigured: kinds.WARNING,
  connecting: kinds.INFO,
  connected: kinds.SUCCESS,
  disconnected: kinds.WARNING,
  failed: kinds.DANGER,
};

// ShowReadOnlyInvite: also the link that adds the bot to another server only to read there (for an import).
interface BotStatusProps {
  setup: SetupStatus;
  showReadOnlyInvite?: boolean;
}

// Whether the bot is connected, why not, and the link that adds it to a Discord server.
function BotStatus({ setup, showReadOnlyInvite = false }: BotStatusProps) {
  const { botState } = setup;
  const state = `BotState${botState[0].toUpperCase()}${botState.slice(1)}`;

  return (
    <FieldSet legend={translate('SettingsBotStatus')}>
      <Alert kind={STATE_KINDS[botState]}>
        {translate(state)}
        {setup.botProblem ? `: ${setup.botProblem}` : ''}
      </Alert>

      {setup.inviteUrl && (
        <div>
          <Link to={setup.inviteUrl}>{translate('SettingsBotInvite')}</Link>
        </div>
      )}

      {showReadOnlyInvite && setup.readOnlyInviteUrl && (
        <div>
          <Link to={setup.readOnlyInviteUrl}>
            {translate('ImportReadInvite')}
          </Link>
          <div className={styles.help}>
            {translate('SettingsBotReadOnlyInviteHelpText')}
          </div>
        </div>
      )}
    </FieldSet>
  );
}

export default BotStatus;
