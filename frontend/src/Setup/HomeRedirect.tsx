import React from 'react';
import { Redirect } from 'react-router-dom';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
import useRequestarrApi from 'Settings/Requestarr/useRequestarrApi';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';

// The web app's home: the first-run setup while a step is left, otherwise System, Status. If the setup can't be
// read, Status still opens, so a broken answer never locks the admin out.
function HomeRedirect() {
  const { data: setup, error } = useRequestarrApi<SetupStatus>('setup');

  if (!setup && !error) {
    return <LoadingIndicator />;
  }

  const path = setup?.needed ? '/setup' : '/system/status';

  return <Redirect to={getPathWithUrlBase(path)} />;
}

export default HomeRedirect;
