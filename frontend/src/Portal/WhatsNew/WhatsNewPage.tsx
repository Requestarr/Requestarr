import React from 'react';
import FieldSet from 'Components/FieldSet';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import translate from 'Utilities/String/translate';
import { useDayText } from '../Components/useDateText';
import { PortalVersion } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Portal.css';

// "**Lead:** the rest": the changelog's bold lead, then its sentence.
const LEAD = /^\*\*(.+?)\*\*\s*(.*)$/;

function Line({ text }: { text: string }) {
  const lead = LEAD.exec(text);

  return (
    <li>
      {lead ? (
        <>
          <strong>{lead[1]}</strong> {lead[2]}
        </>
      ) : (
        text
      )}
    </li>
  );
}

function Section({ title, lines }: { title: string; lines: string[] }) {
  if (!lines.length) {
    return null;
  }

  return (
    <>
      <h4>{title}</h4>
      <ul className={styles.changes}>
        {lines.map((line) => (
          <Line key={line} text={line} />
        ))}
      </ul>
    </>
  );
}

function Versions({ versions }: { versions: PortalVersion[] }) {
  const dayText = useDayText();

  return (
    <>
      {versions.map((version) => (
        <FieldSet
          key={version.version}
          legend={
            version.date
              ? `${version.version} · ${dayText(version.date)}`
              : version.version
          }
        >
          <Section title={translate('PortalForUsers')} lines={version.users} />
          <Section
            title={translate('PortalForAdmins')}
            lines={version.admins}
          />
        </FieldSet>
      ))}
    </>
  );
}

function renderVersions(versions: PortalVersion[]) {
  return <Versions versions={versions} />;
}

// What changed in each version, newest first: the update cards' Read changes, both sections, as the admin channel
// has them.
function WhatsNewPage() {
  const result = usePortal<PortalVersion[]>('changes');

  return (
    <PageContent title={translate('PortalWhatsNew')}>
      <PageContentBody>
        {result.isNotFound ? (
          <NotFound message={translate('PortalNotFound')} />
        ) : (
          <PageSectionContent
            isFetching={result.isLoading}
            isPopulated={result.data !== undefined}
            error={result.hasError ? result : undefined}
            errorMessage={translate('PortalLoadError')}
          >
            {result.data !== undefined && renderVersions(result.data)}
          </PageSectionContent>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default WhatsNewPage;
