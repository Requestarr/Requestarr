import React, { useCallback } from 'react';
import { useHistory } from 'react-router-dom';
import FieldSet from 'Components/FieldSet';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ConnectionCard, {
  AddConnectionCard,
} from '../Connections/ConnectionCard';
import { DiscordServer, SetupStatus } from '../RequestarrSettingsTypes';
import styles from '../Connections/Connections.css';

interface ServerCardProps {
  server: DiscordServer;
  isShown: boolean;
  onShow: (id: string) => void;
}

function ServerCard({ server, isShown, onShow }: ServerCardProps) {
  const onPress = useCallback(() => onShow(server.id), [server.id, onShow]);

  return (
    <ConnectionCard
      name={server.name ?? server.id}
      type={translate(
        server.isOurs ? 'DiscordServerOurs' : 'DiscordServerImportOnly'
      )}
      icon={server.iconUrl}
      labels={
        isShown ? [{ text: translate('ImportShowing'), kind: kinds.INFO }] : []
      }
      onPress={onPress}
    />
  );
}

interface ImportServersProps {
  setup?: SetupStatus;
  shown?: string;
  onAdd: () => void;
}

// The servers the bot can read, as Connections shows them: pressing one shows only its channels below, and the "+"
// card adds the bot to another server only to read (the same Add modal, Import only on).
function ImportServers({ setup, shown, onAdd }: ImportServersProps) {
  const history = useHistory();
  const onShow = useCallback(
    (id?: string) => history.replace({ search: id ? `?server=${id}` : '' }),
    [history]
  );
  const onShowAll = useCallback(() => onShow(), [onShow]);

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
            onShow={onShow}
          />
        ))}
        <AddConnectionCard onPress={onAdd} />
      </div>
      {shown && (
        <Button onPress={onShowAll}>{translate('ImportShowAllServers')}</Button>
      )}
    </FieldSet>
  );
}

export default ImportServers;
