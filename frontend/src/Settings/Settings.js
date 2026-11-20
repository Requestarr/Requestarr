import React from 'react';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import translate from 'Utilities/String/translate';
import SettingsToolbarConnector from './SettingsToolbarConnector';
import styles from './Settings.css';

// Each settings page with its one-line summary: the link's words are the title key, the summary its own key.
const PAGES = [
  { to: '/settings/connections', title: 'SettingsConnections', summary: 'SettingsConnectionsSummary' },
  { to: '/settings/requests', title: 'SettingsRequests', summary: 'SettingsRequestsSummary' },
  { to: '/settings/changes', title: 'SettingsChanges', summary: 'SettingsChangesSummary' },
  { to: '/settings/general', title: 'General', summary: 'GeneralSettingsSummary' },
  { to: '/settings/ui', title: 'UI', summary: 'UISettingsSummary' },
  { to: '/settings/import', title: 'ImportFromRequestrr', summary: 'ImportFromRequestrrSummary' },
  { to: '/setup', title: 'Setup', summary: 'SetupSummary' }
];

function Settings() {
  return (
    <PageContent title={translate('Settings')}>
      <SettingsToolbarConnector
        hasPendingChanges={false}
      />

      <PageContentBody>
        {
          PAGES.map(({ to, title, summary }) => (
            <React.Fragment key={to}>
              <Link
                className={styles.link}
                to={to}
              >
                {translate(title)}
              </Link>

              <div className={styles.summary}>
                {translate(summary)}
              </div>
            </React.Fragment>
          ))
        }
      </PageContentBody>
    </PageContent>
  );
}

Settings.propTypes = {
};

export default Settings;
