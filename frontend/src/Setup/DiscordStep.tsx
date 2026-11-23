import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { kinds } from 'Helpers/Props';
import BotStatus from 'Settings/Requestarr/BotStatus';
import ConnectionCard from 'Settings/Requestarr/Connections/ConnectionCard';
import connectionStyles from 'Settings/Requestarr/Connections/Connections.css';
import AdminPicker from 'Settings/Requestarr/Connections/Discord/AdminPicker';
import DiscordSection from 'Settings/Requestarr/Connections/Discord/DiscordSection';
import { callRequestarrApi } from 'Settings/Requestarr/requestarrApi';
import {
  DiscordServer,
  SetupStatus,
} from 'Settings/Requestarr/RequestarrSettingsTypes';
import { BOT_TOKEN } from 'Settings/Requestarr/settingsPages';
import SettingsSections from 'Settings/Requestarr/SettingsSections';
import useApiAction from 'Settings/Requestarr/useApiAction';
import useSettingsForm from 'Settings/Requestarr/useSettingsForm';
import translate from 'Utilities/String/translate';
import SetupStep from './SetupStep';
import styles from './SetupPage.css';

interface StepProps {
  setup: SetupStatus;
  onSaved: () => void;
}

function PickCard(props: {
  server: DiscordServer;
  onPick: (server: DiscordServer) => void;
}) {
  const { server, onPick } = props;
  const onPress = useCallback(() => onPick(server), [server, onPick]);

  return (
    <ConnectionCard
      name={server.name ?? server.id}
      type={translate('Discord')}
      icon={server.iconUrl}
      labels={[]}
      onPress={onPress}
    />
  );
}

// The bot is in several servers and none is Requestarr's yet: the admin picks one by its name and icon.
function ServerPicker({ setup, onSaved }: StepProps) {
  const pick = useApiAction();
  const { run } = pick;
  const onPick = useCallback(
    (server: DiscordServer) =>
      run(
        () =>
          callRequestarrApi('setup/discord/server', 'POST', { id: server.id }),
        onSaved
      ),
    [run, onSaved]
  );

  return (
    <>
      <p>{translate('SetupPickServer')}</p>
      {pick.problem && <Alert kind={kinds.DANGER}>{pick.problem}</Alert>}
      <div className={connectionStyles.cards}>
        {setup.servers.map((server) => (
          <PickCard key={server.id} server={server} onPick={onPick} />
        ))}
      </div>
    </>
  );
}

// Before the bot has a server: Discord's invite page, and the page waits for the join (live, no refresh).
function Invite({ setup }: { setup: SetupStatus }) {
  const onInvite = useCallback(
    () => window.open(setup.inviteUrl, '_blank', 'noopener'),
    [setup.inviteUrl]
  );

  return (
    <>
      <Button
        kind={kinds.PRIMARY}
        isDisabled={!setup.inviteUrl}
        onPress={onInvite}
      >
        {translate('SettingsBotInvite')}
      </Button>
      <p>{translate('SetupWaitingForServer')}</p>
    </>
  );
}

// Once the bot is in Requestarr's server: who joined where, the servers as Connections shows them, and the admins.
function Joined({ setup, onSaved }: StepProps) {
  const ours = setup.servers.find((server) => server.isOurs);
  const settings = { provisioned: setup.provisioned, values: {} };

  return (
    <>
      <Alert kind={kinds.SUCCESS}>
        {translate('SetupBotJoined', { bot: setup.bot?.name ?? '' })}{' '}
        <strong>{ours?.name}</strong>
      </Alert>
      <DiscordSection setup={setup} settings={settings} onSaved={onSaved} />
      <AdminPicker isReadOnly={setup.provisioned} />
    </>
  );
}

function Server({ setup, onSaved }: StepProps) {
  if (setup.servers.some((server) => server.isOurs)) {
    return <Joined setup={setup} onSaved={onSaved} />;
  }

  return setup.pickServer ? (
    <ServerPicker setup={setup} onSaved={onSaved} />
  ) : (
    <Invite setup={setup} />
  );
}

// The bot: only its token is typed (the setup guide, section 2). Saving connects it, Requestarr reads its
// application from Discord and shows its name and picture, then the admin adds it to their server (or picks one it's
// already in), and picks the admins, all here.
function DiscordStep({ setup, onSaved }: StepProps) {
  const form = useSettingsForm(onSaved);

  return (
    <SetupStep number={2} title="SetupDiscord" isDone={setup.discord}>
      <div className={styles.intro}>{translate('SetupDiscordIntro')}</div>

      {form.settings && <SettingsSections sections={[BOT_TOKEN]} form={form} />}

      {form.saveError && <Alert kind={kinds.DANGER}>{form.saveError}</Alert>}

      <SpinnerButton
        kind={kinds.PRIMARY}
        isSpinning={form.isSaving}
        isDisabled={!form.hasChanges}
        onPress={form.onSave}
      >
        {translate('Save')}
      </SpinnerButton>

      {setup.botState !== 'notConfigured' && <BotStatus setup={setup} />}

      {setup.bot && <Server setup={setup} onSaved={onSaved} />}
    </SetupStep>
  );
}

export default DiscordStep;
