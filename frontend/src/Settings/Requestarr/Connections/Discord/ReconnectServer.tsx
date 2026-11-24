import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { DiscordServer } from '../../RequestarrSettingsTypes';
import useBotJoin from './useBotJoin';
import WaitingForBot from './WaitingForBot';

interface ReconnectServerProps {
  server: DiscordServer;
  onJoined: () => void;
}

// For a server's window: while the bot isn't in it any more, why its card is red and Discord's invite with that server
// already picked; nothing while it is. It stays in the window either way, so it notices the bot coming back.
function ReconnectServer({ server, onJoined }: ReconnectServerProps) {
  const { reconnectUrl } = server;
  const [isWaiting, setIsWaiting] = useState(false);
  const onReconnect = useCallback(() => {
    window.open(reconnectUrl, '_blank', 'noopener');
    setIsWaiting(true);
  }, [reconnectUrl]);

  useBotJoin([server], isWaiting, onJoined);

  if (server.joined) {
    return null;
  }

  return (
    <>
      <Alert kind={kinds.WARNING}>{translate('DiscordServerRemovedBot')}</Alert>
      {isWaiting && <WaitingForBot />}
      <Button
        kind={kinds.PRIMARY}
        isDisabled={!reconnectUrl}
        onPress={onReconnect}
      >
        {translate('DiscordReconnect')}
      </Button>
    </>
  );
}

export default ReconnectServer;
