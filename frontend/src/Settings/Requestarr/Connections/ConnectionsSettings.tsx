import React, { useCallback, useEffect } from 'react';
import { useLocation } from 'react-router-dom';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import BotStatus from '../BotStatus';
import { AppSettings, Instance, SetupStatus } from '../RequestarrSettingsTypes';
import {
  BUG_REPORT_SECTIONS,
  CONNECTION_OPTIONS,
  DISCORD_SECTIONS,
  TMDB_SECTIONS,
} from '../settingsPages';
import SettingsSections from '../SettingsSections';
import useRequestarrApi from '../useRequestarrApi';
import useSettingsForm, { SettingsForm } from '../useSettingsForm';
import { discordLabels, keyLabels } from './connectionLabels';
import {
  MediaManagersSection,
  SettingsCardSection,
} from './ConnectionSections';
import ConnectionsToolbar, { useShowAdvanced } from './ConnectionsToolbar';
import useTestAll from './useTestAll';
import styles from './Connections.css';

// What Test All Connections found: each failure on its own line, or that everything passed.
function TestAllResult({ problems }: { problems?: string[] }) {
  if (!problems) {
    return null;
  }

  if (!problems.length) {
    return (
      <Alert kind={kinds.SUCCESS}>
        {translate('TestAllConnectionsPassed')}
      </Alert>
    );
  }

  return (
    <Alert kind={kinds.DANGER}>
      <ul className={styles.problems}>
        {problems.map((problem) => (
          <li key={problem}>{problem}</li>
        ))}
      </ul>
    </Alert>
  );
}

// An old address (the Discord page, the Sonarr and Radarr page) lands on its section here.
function useScrollToSection(isLoaded: boolean) {
  const { hash } = useLocation();

  useEffect(() => {
    if (isLoaded && hash) {
      document.getElementById(hash.slice(1))?.scrollIntoView();
    }
  }, [isLoaded, hash]);
}

interface ConnectionCardsProps {
  settings: AppSettings;
  setup?: SetupStatus;
  instances?: Instance[];
  options: SettingsForm;
  showAdvanced: boolean;
  onSaved: () => void;
}

// The sections in the owner's order: Discord, Media managers, Metadata, Bug reports, then Options.
function ConnectionCards({
  settings,
  setup,
  instances,
  options,
  showAdvanced,
  onSaved,
}: ConnectionCardsProps) {
  const cards = { settings, showAdvanced, onSaved };

  return (
    <>
      <SettingsCardSection
        id="discord"
        legend={translate('SettingsDiscord')}
        name={translate('ConnectionDiscordBot')}
        type={translate('Discord')}
        labels={discordLabels(setup, settings)}
        sections={DISCORD_SECTIONS}
        {...cards}
      >
        {setup && <BotStatus setup={setup} showReadOnlyInvite={true} />}
      </SettingsCardSection>

      <MediaManagersSection
        instances={instances}
        settings={settings}
        onSaved={onSaved}
      />

      <SettingsCardSection
        id="metadata"
        legend={translate('ConnectionsMetadata')}
        name={translate('SettingsTmdb')}
        type={translate('ConnectionsMetadata')}
        labels={keyLabels(settings, 'tmdbApiKey')}
        sections={TMDB_SECTIONS}
        {...cards}
      />

      <SettingsCardSection
        id="bug-reports"
        legend={translate('SettingsBugReports')}
        name={translate('ConnectionGitHub')}
        type={translate('SettingsBugReports')}
        labels={keyLabels(
          settings,
          'bugReportsGithubToken',
          !!settings.values.bugReportsFileIssues
        )}
        sections={BUG_REPORT_SECTIONS}
        {...cards}
      />

      <SettingsSections
        sections={[CONNECTION_OPTIONS]}
        form={options}
        showAdvanced={showAdvanced}
      />
    </>
  );
}

// Everything Requestarr connects to, built like Sonarr's Indexers settings: a section of cards for each kind of
// connection, each card opening its edit modal, and the Options form below. Read-only where the provisioning file
// sets it (dev and QA).
function ConnectionsSettings() {
  const options = useSettingsForm();
  const instances = useRequestarrApi<Instance[]>('instances');
  const setup = useRequestarrApi<SetupStatus>('setup');
  const showAdvanced = useShowAdvanced();
  const { settings, reload } = options;
  const error = options.error ?? instances.error ?? setup.error;

  const onSaved = useCallback(() => {
    reload();
    instances.reload();
    setup.reload();
  }, [reload, instances, setup]);

  const testAll = useTestAll(instances.data, onSaved);
  useScrollToSection(!!settings && !!instances.data);

  return (
    <PageContent title={translate('SettingsConnections')}>
      <ConnectionsToolbar options={options} {...testAll} />

      <PageContentBody>
        {!settings && !error && <LoadingIndicator />}
        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
        {options.saveError && (
          <Alert kind={kinds.DANGER}>{options.saveError}</Alert>
        )}

        {settings?.provisioned && (
          <Alert kind={kinds.INFO}>
            {translate('SettingsProvisionedNotice')}
          </Alert>
        )}

        <TestAllResult problems={testAll.problems} />

        {settings && (
          <ConnectionCards
            settings={settings}
            setup={setup.data}
            instances={instances.data}
            options={options}
            showAdvanced={showAdvanced}
            onSaved={onSaved}
          />
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default ConnectionsSettings;
