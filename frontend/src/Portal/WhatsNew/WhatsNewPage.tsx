import React from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import PortalPage from '../Components/PortalPage';
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
    <PortalPage title={translate('PortalWhatsNew')} result={result}>
      {renderVersions}
    </PortalPage>
  );
}

export default WhatsNewPage;
