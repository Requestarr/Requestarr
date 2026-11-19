import React from 'react';
import Alert from 'Components/Alert';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import BotStatus from 'Settings/Requestarr/BotStatus';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
import {
  DISCORD_CONNECTION,
  THIS_SERVER,
} from 'Settings/Requestarr/settingsPages';
import SettingsSections from 'Settings/Requestarr/SettingsSections';
import useSettingsForm from 'Settings/Requestarr/useSettingsForm';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './SetupPage.css';

const SECTIONS = [THIS_SERVER, DISCORD_CONNECTION];

interface DiscordStepProps {
  setup: SetupStatus;
  onSaved: () => void;
}

// The bot's token and IDs, from the Discord Developer Portal and the server (the setup guide, section 2). Saving
// connects the bot; its status and the link that adds it to the server show below.
function DiscordStep({ setup, onSaved }: DiscordStepProps) {
  const form = useSettingsForm(onSaved);

  return (
    <SetupStep number={2} title="SetupDiscord" isDone={setup.discord}>
      <div className={styles.intro}>{translate('SetupDiscordIntro')}</div>

      {form.settings && <SettingsSections sections={SECTIONS} form={form} />}

      {form.saveError && <Alert kind={kinds.DANGER}>{form.saveError}</Alert>}

      <SpinnerButton
        kind={kinds.PRIMARY}
        isSpinning={form.isSaving}
        isDisabled={!form.hasChanges}
        onPress={form.onSave}
      >
        {translate('Save')}
      </SpinnerButton>

      {setup.discord && <BotStatus setup={setup} />}
    </SetupStep>
  );
}

export default DiscordStep;
