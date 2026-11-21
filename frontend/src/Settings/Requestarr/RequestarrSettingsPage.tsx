import React, { ReactNode } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { SettingsSection } from './RequestarrSettingsTypes';
import SettingsSections from './SettingsSections';
import SettingsToolbar from './settingsToolbar';
import useSettingsForm from './useSettingsForm';

interface RequestarrSettingsPageProps {
  title: string;
  sections: SettingsSection[];
  children?: ReactNode;
  onSaved?: () => void;
}

// A page of Requestarr's own settings, as Servarr's General settings page: Servarr's settings toolbar (Save Changes,
// and the prompt when leaving with changes unsaved), read-only where the provisioning file sets them (dev and QA),
// each change recorded on the Changes page.
function RequestarrSettingsPage({
  title,
  sections,
  children,
  onSaved,
}: RequestarrSettingsPageProps) {
  const form = useSettingsForm(onSaved);
  const { settings, error, hasChanges, isSaving, saveError, onSave } = form;

  return (
    <PageContent title={title}>
      <SettingsToolbar
        hasPendingChanges={hasChanges}
        isSaving={isSaving}
        onSavePress={onSave}
      />

      <PageContentBody>
        {!settings && !error && <LoadingIndicator />}

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}

        {settings?.provisioned && (
          <Alert kind={kinds.INFO}>
            {translate('SettingsProvisionedNotice')}
          </Alert>
        )}

        {saveError && <Alert kind={kinds.DANGER}>{saveError}</Alert>}

        {children}

        {settings && <SettingsSections sections={sections} form={form} />}
      </PageContentBody>
    </PageContent>
  );
}

export default RequestarrSettingsPage;
