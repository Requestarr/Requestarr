import React, { useCallback, useEffect, useMemo } from 'react';
import { useSelector } from 'react-redux';
import { useLocation } from 'react-router-dom';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import BotStatus from '../BotStatus';
import { AppSettings, SetupStatus } from '../RequestarrSettingsTypes';
import { CONNECTION_OPTIONS, DISCORD_SECTIONS } from '../settingsPages';
import SettingsSections from '../SettingsSections';
import useRequestarrApi from '../useRequestarrApi';
import useSettingsForm, { SettingsForm } from '../useSettingsForm';
import { discordLabels, TestOutcome } from './connectionLabels';
import { MediaServersSection, SettingsCardSection } from './ConnectionSections';
import ConnectionsToolbar, { useShowAdvanced } from './ConnectionsToolbar';
import MediaManagersSection from './MediaManagers/MediaManagersSection';
import styles from './Connections.css';

// What Test All Connections found: each failure on its own line, that everything passed, or that nothing was turned
// on to test.
function TestAllResult({ outcomes }: { outcomes?: TestOutcome[] }) {
  if (!outcomes) {
    return null;
  }

  if (!outcomes.length) {
    return (
      <Alert kind={kinds.INFO}>{translate('TestAllConnectionsNothing')}</Alert>
    );
  }

  const failed = outcomes.filter((outcome) => outcome.problem);

  if (!failed.length) {
    return (
      <Alert kind={kinds.SUCCESS}>
        {translate('TestAllConnectionsPassed')}
      </Alert>
    );
  }

  return (
    <Alert kind={kinds.DANGER}>
      <ul className={styles.problems}>
        {failed.map(({ id, name, problem }) => (
          <li key={id}>{`${name}: ${problem}`}</li>
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
  outcomes?: TestOutcome[];
  options: SettingsForm;
  showAdvanced: boolean;
  onSaved: () => void;
}

// The sections in the owner's order: Discord, Media managers, Media servers, then Options. The Movie Database and
// GitHub keys aren't here: they come from the server's environment, never the web app.
function ConnectionCards({
  settings,
  setup,
  outcomes,
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

      <MediaManagersSection settings={settings} outcomes={outcomes} />

      <MediaServersSection />

      <SettingsSections
        sections={[CONNECTION_OPTIONS]}
        form={options}
        showAdvanced={showAdvanced}
      />
    </>
  );
}

// What Test All Connections found, one outcome per instance tested, named as its card is; undefined until it has run.
function useTestAllOutcomes(): TestOutcome[] | undefined {
  const { items, testAllResults } = useSelector(
    (state: AppState) => state.settings.mediaManagers
  );

  return useMemo(
    () =>
      testAllResults?.map(({ id, validationFailures }) => ({
        id,
        name: items.find((item) => item.id === id)?.name ?? '',
        problem: validationFailures[0]?.errorMessage,
      })),
    [items, testAllResults]
  );
}

// Everything Requestarr connects to, built like Sonarr's Indexers settings: a section of cards for each kind of
// connection, each card opening its edit modal, and the Options form below. Read-only where the provisioning file
// sets it (dev and QA).
function ConnectionsSettings() {
  const options = useSettingsForm();
  const setup = useRequestarrApi<SetupStatus>('setup');
  const showAdvanced = useShowAdvanced();
  const outcomes = useTestAllOutcomes();
  const { settings, reload } = options;
  const { reload: reloadSetup } = setup;
  const error = options.error ?? setup.error;

  // The hooks' reloads keep their identity, so onSaved does too (the hooks' results are new objects each render).
  const onSaved = useCallback(() => {
    reload();
    reloadSetup();
  }, [reload, reloadSetup]);

  useScrollToSection(!!settings);

  return (
    <PageContent title={translate('SettingsConnections')}>
      <ConnectionsToolbar options={options} />

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

        <TestAllResult outcomes={outcomes} />

        {settings && (
          <ConnectionCards
            settings={settings}
            setup={setup.data}
            outcomes={outcomes}
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
