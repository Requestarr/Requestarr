import { useEffect } from 'react';
import { useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import { SetupStatus } from './RequestarrSettingsTypes';
import useRequestarrApi from './useRequestarrApi';

// The setup's answer (the bot, its servers, which steps are done), read again whenever the server says the bot or
// its servers changed or a Sonarr or Radarr was added or changed, as Sonarr's pages follow its live updates: the bot
// joining a server shows without a refresh.
function useSetupStatus() {
  const setup = useRequestarrApi<SetupStatus>('setup');
  const changed = useSelector((state: AppState) => state.app.setupChanged);
  const instances = useSelector(
    (state: AppState) => state.settings.mediaManagers.items
  );
  const { reload } = setup;

  useEffect(() => {
    if (changed) {
      reload();
    }
  }, [changed, reload]);

  useEffect(() => {
    reload();
  }, [instances, reload]);

  return setup;
}

export default useSetupStatus;
