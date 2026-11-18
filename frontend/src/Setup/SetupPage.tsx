import React from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import { SetupStatus } from 'Settings/Requestarr/RequestarrSettingsTypes';
import useRequestarrApi from 'Settings/Requestarr/useRequestarrApi';
import translate from 'Utilities/String/translate';
import DiscordStep from './DiscordStep';
import InstanceStep from './InstanceStep';
import SetupStep from './SetupStep';
import styles from './SetupPage.css';

// The first-run setup of a new install without a provisioning file (beta and production): the admin's sign-in, the
// Discord bot, and a first Sonarr or Radarr. Everything else has working defaults and lives under Settings.
function SetupPage() {
  const { data: setup, error, reload } = useRequestarrApi<SetupStatus>('setup');
  const isReady =
    setup && setup.adminAccount && setup.discord && setup.instance;

  return (
    <PageContent title={translate('Setup')}>
      <PageContentBody>
        {!setup && !error && <LoadingIndicator />}

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}

        {setup && (
          <>
            <div className={styles.intro}>{translate('SetupIntro')}</div>

            <SetupStep
              number={1}
              title="SetupAdminAccount"
              isDone={setup.adminAccount}
            >
              {translate('SetupAdminAccountIntro')}
            </SetupStep>

            <DiscordStep setup={setup} onSaved={reload} />

            <InstanceStep setup={setup} onSaved={reload} />

            {isReady ? (
              <Alert kind={kinds.SUCCESS}>
                {translate('SetupReady')}{' '}
                <Link to="/settings">{translate('Settings')}</Link>
              </Alert>
            ) : (
              <Button onPress={reload}>{translate('SetupCheckAgain')}</Button>
            )}
          </>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default SetupPage;
