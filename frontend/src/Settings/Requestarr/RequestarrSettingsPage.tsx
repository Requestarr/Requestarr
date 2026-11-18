import React, { ReactNode } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { SettingsSection } from './RequestarrSettingsTypes';
import SettingsSections from './SettingsSections';
import useSettingsForm from './useSettingsForm';

interface RequestarrSettingsPageProps {
  title: string;
  sections: SettingsSection[];
  children?: ReactNode;
  onSaved?: () => void;
}

// A page of Requestarr's own settings: read-only where the provisioning file sets them (dev and QA), otherwise saved
// with Save Changes and recorded on the Changes page.
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
      <PageToolbar>
        <PageToolbarSection>
          <PageToolbarButton
            label={
              hasChanges ? translate('SaveChanges') : translate('NoChanges')
            }
            iconName={icons.SAVE}
            isSpinning={isSaving}
            isDisabled={!hasChanges}
            onPress={onSave}
          />
        </PageToolbarSection>
      </PageToolbar>

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
