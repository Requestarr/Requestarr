import { useEffect, useRef } from 'react';
import { DiscordServer } from '../../RequestarrSettingsTypes';

function joinedIds(servers: DiscordServer[]) {
  return servers.filter((server) => server.joined).map((server) => server.id);
}

// Calls back once the bot joins one of these servers (they follow the bot live), from when Discord's invite page was
// opened: until then, a server that turns up is only the bot connecting.
function useBotJoin(
  servers: DiscordServer[],
  isWaiting: boolean,
  onJoin: () => void
) {
  const before = useRef(joinedIds(servers));

  useEffect(() => {
    const joined = joinedIds(servers);

    if (isWaiting && joined.some((id) => !before.current.includes(id))) {
      onJoin();
    }

    before.current = joined;
  }, [servers, isWaiting, onJoin]);
}

export default useBotJoin;
