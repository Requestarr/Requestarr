import React, { useCallback, useState } from 'react';
import { useHistory } from 'react-router-dom';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ConnectionCard, {
  AddConnectionCard,
  CardLabel,
} from '../Connections/ConnectionCard';
import { serverReach } from '../Connections/connectionLabels';
import ServerModal from '../Connections/Discord/ServerModal';
import { DiscordServer, SetupStatus } from '../RequestarrSettingsTypes';
import styles from '../Connections/Connections.css';

interface ServerCardProps {
  server: DiscordServer;
  isShown: boolean;
  onOpen: (id: string) => void;
}

function ServerCard({ server, isShown, onOpen }: ServerCardProps) {
  const onPress = useCallback(() => onOpen(server.id), [server.id, onOpen]);
  const showing: CardLabel[] = isShown
    ? [{ text: translate('ImportShowing'), kind: kinds.INFO }]
    : [];

  return (
    <ConnectionCard
      name={server.name ?? translate('DiscordServerUnknown')}
      type={translate(
        server.isOurs ? 'DiscordServerOurs' : 'DiscordServerImportOnly'
      )}
      icon={server.iconUrl}
      labels={[serverReach(server), ...showing]}
      onPress={onPress}
    />
  );
}

interface ImportServersProps {
  setup?: SetupStatus;
  shown?: string;
  onAdd: () => void;
}

// The servers the bot can read, as Connections shows them: pressing one opens its window (its channels only, taking the
// bot out of it, or adding it back), and the "+" card adds the bot to another server only to read (the same Add modal,
// Import only on).
function ImportServers({ setup, shown, onAdd }: ImportServersProps) {
  const history = useHistory();
  const [openId, setOpenId] = useState<string>();
  const onShowAll = useCallback(
    () => history.replace({ search: '' }),
    [history]
  );
  const onClose = useCallback(() => setOpenId(undefined), []);
  const onRemoved = useCallback(() => {
    onClose();

    if (openId === shown) {
      onShowAll();
    }
  }, [openId, shown, onClose, onShowAll]);

  if (!setup) {
    return null;
  }

  return (
    <FieldSet legend={translate('ImportServers')}>
      <div className={styles.cards}>
        {setup.servers.map((server) => (
          <ServerCard
            key={server.id}
            server={server}
            isShown={server.id === shown}
            onOpen={setOpenId}
          />
        ))}
        <AddConnectionCard onPress={onAdd} />
      </div>
      {shown && (
        <Button onPress={onShowAll}>{translate('ImportShowAllServers')}</Button>
      )}

      <ServerModal
        server={setup.servers.find((server) => server.id === openId)}
        channelsLabel={translate('ImportShowServerChannels')}
        onClose={onClose}
        onRemoved={onRemoved}
      />
    </FieldSet>
  );
}

export default ImportServers;
