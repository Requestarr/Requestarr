import React from 'react';
import FieldSet from 'Components/FieldSet';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import updateStyles from 'System/Updates/UpdateChanges.css';
import translate from 'Utilities/String/translate';
import { useDayText } from '../Components/useDateText';
import { PortalChange, PortalVersion } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Portal.css';

// A version's groups, in the order Sonarr's System, Updates page shows New and Fixed.
const KINDS: { kind: PortalChange['kind']; title: string }[] = [
  { kind: 'new', title: 'New' },
  { kind: 'fixed', title: 'Fixed' },
  { kind: 'changed', title: 'Changed' },
];

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

// One kind's lines under its heading, as Sonarr's update changes are grouped, with its heading style. Servarr's own list
// shows links and code but not bold, so it can't show a bold lead.
function Group({ title, lines }: { title: string; lines: PortalChange[] }) {
  if (!lines.length) {
    return null;
  }

  return (
    <>
      <div className={updateStyles.title}>{title}</div>
      <ul className={styles.changes}>
        {lines.map(({ text }) => (
          <Line key={text} text={text} />
        ))}
      </ul>
    </>
  );
}

function Section({ title, lines }: { title: string; lines: PortalChange[] }) {
  if (!lines.length) {
    return null;
  }

  return (
    <>
      <h4>{title}</h4>
      {KINDS.map(({ kind, title: group }) => (
        <Group
          key={kind}
          title={translate(group)}
          lines={lines.filter((line) => line.kind === kind)}
        />
      ))}
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
// has them, each grouped into New, Fixed and Changed.
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
