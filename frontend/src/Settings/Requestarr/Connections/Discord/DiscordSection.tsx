import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import BotStatus from '../../BotStatus';
import {
  AppSettings,
  DiscordServer,
  SetupStatus,
} from '../../RequestarrSettingsTypes';
import { DISCORD_SECTIONS } from '../../settingsPages';
import ConnectionCard, {
  AddConnectionCard,
  CardLabel,
} from '../ConnectionCard';
import { discordLabels, serverReach } from '../connectionLabels';
import SettingsConnectionModal from '../SettingsConnectionModal';
import AddServerModal from './AddServerModal';
import AdminPicker from './AdminPicker';
import BetaServer from './BetaServer';
import ReconnectServer from './ReconnectServer';
import ServerModal from './ServerModal';
import styles from '../Connections.css';

function serverLabels(
  server: DiscordServer,
  settings: AppSettings
): CardLabel[] {
  const readOnly: CardLabel[] = server.isOurs
    ? []
    : [{ text: translate('DiscordServerReadOnly'), kind: kinds.DISABLED }];
  const provisioned: CardLabel[] = settings.provisioned
    ? [{ text: translate('ConnectionProvisioned'), kind: kinds.DISABLED }]
    : [];

  return [serverReach(server), ...readOnly, ...provisioned];
}

function ServerCard(props: {
  server: DiscordServer;
  settings: AppSettings;
  onOpen: (server: DiscordServer) => void;
}) {
  const { server, settings, onOpen } = props;
  const onPress = useCallback(() => onOpen(server), [server, onOpen]);

  return (
    <ConnectionCard
      name={server.name ?? translate('DiscordServerUnknown')}
      type={translate(
        server.isOurs ? 'DiscordServerOurs' : 'DiscordServerImportOnly'
      )}
      icon={server.iconUrl}
      labels={serverLabels(server, settings)}
      onPress={onPress}
    />
  );
}

interface DiscordSectionProps {
  setup: SetupStatus;
  settings: AppSettings;
  onSaved: () => void;
}

// The Discord section, as Media managers shows its instances: a card for each server the bot is in (Requestarr's
// own, and any it only reads for an import), the bot's card while it has no server yet, and the "+" card that adds
// it to a server. Requestarr's server (or the bot's card) opens the bot's modal: its token, admins, picture and
// banner, and Show Advanced for an admin role of the admin's own.
function DiscordSection({ setup, settings, onSaved }: DiscordSectionProps) {
  const [isEditing, setIsEditing] = useState(false);
  const [isAdding, setIsAdding] = useState(false);
  const [openId, setOpenId] = useState<string>();
  const ours = setup.servers.find((server) => server.isOurs);
  const open = setup.servers.find((server) => server.id === openId);
  const onOpen = useCallback((server: DiscordServer) => {
    if (server.isOurs) {
      setIsEditing(true);
    } else {
      setOpenId(server.id);
    }
  }, []);
  const onEdit = useCallback(() => setIsEditing(true), []);
  const onEditClose = useCallback(() => setIsEditing(false), []);
  const onAdd = useCallback(() => setIsAdding(true), []);
  const onAddClose = useCallback(() => setIsAdding(false), []);
  const onServerClose = useCallback(() => setOpenId(undefined), []);
  const onSavedClose = useCallback(() => {
    setIsEditing(false);
    onSaved();
  }, [onSaved]);

  // Its card went away meanwhile (forgotten in another window): it doesn't open again by itself when it's back.
  if (openId && !open) {
    setOpenId(undefined);
  }

  return (
    <div id="discord">
      <FieldSet legend={translate('SettingsDiscord')}>
        <div className={styles.cards}>
          {!ours && (
            <ConnectionCard
              name={setup.bot?.name ?? translate('ConnectionDiscordBot')}
              type={translate('Discord')}
              icon={setup.bot?.avatarUrl}
              labels={discordLabels(setup, settings)}
              onPress={onEdit}
            />
          )}
          {setup.servers.map((server) => (
            <ServerCard
              key={server.id}
              server={server}
              settings={settings}
              onOpen={onOpen}
            />
          ))}
          <AddConnectionCard onPress={onAdd} />
        </div>
      </FieldSet>

      <SettingsConnectionModal
        isOpen={isEditing}
        title={translate('InstanceEditTitle', {
          name: ours?.name ?? translate('ConnectionDiscordBot'),
        })}
        sections={DISCORD_SECTIONS}
        onSaved={onSavedClose}
        onClose={onEditClose}
      >
        <BotStatus setup={setup} />
        {ours && <ReconnectServer server={ours} onJoined={onEditClose} />}
        {setup.betaFromImage && <BetaServer />}
        {ours?.joined && <AdminPicker isReadOnly={settings.provisioned} />}
      </SettingsConnectionModal>

      <AddServerModal
        isOpen={isAdding}
        setup={setup}
        importOnly={false}
        onClose={onAddClose}
      />

      <ServerModal
        server={open}
        channelsLabel={translate('DiscordImportRequests')}
        onClose={onServerClose}
        onRemoved={onServerClose}
      />
    </div>
  );
}

export default DiscordSection;
