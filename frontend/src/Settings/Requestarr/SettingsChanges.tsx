import React from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import TimelineTable from 'Portal/Components/TimelineTable';
import { PortalEvent } from 'Portal/PortalTypes';
import translate from 'Utilities/String/translate';
import useRequestarrApi from './useRequestarrApi';

// Every change made to Requestarr's settings and its Sonarr and Radarr instances, newest first: who, when, and from
// what to what.
function SettingsChanges() {
  const { data, error } = useRequestarrApi<PortalEvent[]>('settings/changes');

  return (
    <PageContent title={translate('SettingsChanges')}>
      <PageContentBody>
        {!data && !error && <LoadingIndicator />}

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}

        {data && <TimelineTable events={data} />}
      </PageContentBody>
    </PageContent>
  );
}

export default SettingsChanges;
