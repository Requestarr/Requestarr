import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InstanceModal, { InstanceEdit } from './InstanceModal';
import InstancesTable from './InstancesTable';
import { AppSettings, Instance } from './RequestarrSettingsTypes';
import useRequestarrApi from './useRequestarrApi';

// Requestarr's Sonarr and Radarr instances: add one, or open one to change it. Read-only where the provisioning file
// lists them (dev and QA).
function InstancesSettings() {
  const {
    data: instances,
    error,
    reload,
  } = useRequestarrApi<Instance[]>('instances');
  const { data: settings } = useRequestarrApi<AppSettings>('settings');
  const [edit, setEdit] = useState<InstanceEdit>();
  const isReadOnly = settings?.provisioned ?? true;

  const onAddSonarrPress = useCallback(
    () => setEdit({ implementation: 'Sonarr' }),
    []
  );
  const onAddRadarrPress = useCallback(
    () => setEdit({ implementation: 'Radarr' }),
    []
  );
  const onClose = useCallback(() => setEdit(undefined), []);

  const onOpen = useCallback((instance: Instance) => {
    setEdit({ instance, implementation: instance.settings.implementation });
  }, []);

  const onSaved = useCallback(() => {
    setEdit(undefined);
    reload();
  }, [reload]);

  return (
    <PageContent title={translate('SettingsInstances')}>
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={translate('InstanceAddSonarr')}
            iconName={icons.ADD}
            isDisabled={isReadOnly}
            onPress={onAddSonarrPress}
          />
          <PageToolbarButton
            label={translate('InstanceAddRadarr')}
            iconName={icons.ADD}
            isDisabled={isReadOnly}
            onPress={onAddRadarrPress}
          />
        </PageToolbarSection>
      </PageToolbar>

      <PageContentBody>
        {!instances && !error && <LoadingIndicator />}

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}

        {settings?.provisioned && (
          <Alert kind={kinds.INFO}>
            {translate('SettingsProvisionedNotice')}
          </Alert>
        )}

        {instances && <InstancesTable instances={instances} onOpen={onOpen} />}

        <InstanceModal
          edit={edit}
          isReadOnly={isReadOnly}
          onSaved={onSaved}
          onClose={onClose}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default InstancesSettings;
