import React from 'react';
import FieldSet from 'Components/FieldSet';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import UpdateChanges from 'System/Updates/UpdateChanges';
import translate from 'Utilities/String/translate';
import { useDayText } from '../Components/useDateText';
import { PortalChange, PortalVersion } from '../PortalTypes';
import usePortal from '../usePortal';

// A version's groups, in the order Sonarr's System, Updates page shows New and Fixed.
const KINDS: { kind: PortalChange['kind']; title: string }[] = [
  { kind: 'new', title: 'New' },
  { kind: 'fixed', title: 'Fixed' },
  { kind: 'changed', title: 'Changed' },
];

// One audience's lines, each group with Servarr's update changes list (it reads the bold lead).
function Section({ title, lines }: { title: string; lines: PortalChange[] }) {
  if (!lines.length) {
    return null;
  }

  return (
    <>
      <h4>{title}</h4>
      {KINDS.map(({ kind, title: group }) => (
        <UpdateChanges
          key={kind}
          title={translate(group)}
          changes={lines.filter((l) => l.kind === kind).map((l) => l.text)}
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
