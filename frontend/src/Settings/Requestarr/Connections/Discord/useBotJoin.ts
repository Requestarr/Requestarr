import { useEffect, useRef } from 'react';
import { DiscordServer } from '../../RequestarrSettingsTypes';

function joinedIds(servers: DiscordServer[]) {
  return servers.filter((server) => server.joined).map((server) => server.id);
}

// Calls back once the bot has joined one of these servers it wasn't in when the window opened (the servers follow the
// bot live).
function useBotJoin(servers: DiscordServer[], onJoin: () => void) {
  const before = useRef(joinedIds(servers));

  useEffect(() => {
    if (joinedIds(servers).some((id) => !before.current.includes(id))) {
      onJoin();
    }
  }, [servers, onJoin]);
}

export default useBotJoin;
