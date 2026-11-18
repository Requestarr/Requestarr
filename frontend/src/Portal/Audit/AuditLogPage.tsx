import React, { useCallback, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import columnsOf from '../Components/columnsOf';
import PortalPage from '../Components/PortalPage';
import { PortalAuditEntry } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Portal.css';

const FILTERS = ['member', 'title', 'action', 'from', 'to'] as const;

type Filters = Record<(typeof FILTERS)[number], string>;

const NO_FILTERS: Filters = {
  member: '',
  title: '',
  action: '',
  from: '',
  to: '',
};

const columns = columnsOf([
  'PortalWhen',
  'PortalWhat',
  'PortalWho',
  'PortalDetail',
  'PortalAbout',
]);

function About({ entry }: { entry: PortalAuditEntry }) {
  if (entry.requestId) {
    return <Link to={`/requests/${entry.requestId}`}>{entry.about}</Link>;
  }

  return entry.memberId ? (
    <Link to={`/members/${entry.memberId}`}>{entry.about}</Link>
  ) : (
    <>{entry.about ?? ''}</>
  );
}

function renderEntries(entries: PortalAuditEntry[]) {
  if (!entries.length) {
    return (
      <div className={styles.empty}>{translate('PortalNothingFound')}</div>
    );
  }

  return (
    <Table columns={columns}>
      <TableBody>
        {entries.map((entry, index) => (
          // eslint-disable-next-line react/no-array-index-key -- entries can share a time and words; the list is never reordered on the page
          <TableRow key={index}>
            <RelativeDateCell date={entry.at} />
            <TableRowCell>{entry.what}</TableRowCell>
            <TableRowCell>{entry.who}</TableRowCell>
            <TableRowCell>{entry.detail ?? ''}</TableRowCell>
            <TableRowCell>
              <About entry={entry} />
            </TableRowCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// Everything Requestarr recorded, newest first: find it by member, title, action and day.
function AuditLogPage() {
  const [typed, setTyped] = useState(NO_FILTERS);
  const [applied, setApplied] = useState(NO_FILTERS);
  const query = new URLSearchParams(
    Object.entries(applied).filter(([, value]) => value)
  ).toString();
  const result = usePortal<PortalAuditEntry[]>(`audit?${query}`);

  const onChange = useCallback(
    ({ name, value }: { name: string; value: string }) => {
      setTyped((current) => ({ ...current, [name]: value }));
    },
    []
  );
  const onSearchPress = useCallback(() => setApplied(typed), [typed]);

  return (
    <PortalPage title={translate('PortalAuditLog')} result={result}>
      {(entries) => (
        <>
          <div className={styles.filters}>
            {FILTERS.map((name) => (
              <label key={name} className={styles.filter}>
                <span className={styles.filterLabel}>
                  {translate(
                    `PortalAuditFilter${name[0].toUpperCase()}${name.slice(1)}`
                  )}
                </span>
                <TextInput
                  type={name === 'from' || name === 'to' ? 'date' : 'text'}
                  name={name}
                  value={typed[name]}
                  onChange={onChange}
                />
              </label>
            ))}
            <Button kind={kinds.PRIMARY} onPress={onSearchPress}>
              {translate('Search')}
            </Button>
          </div>
          {renderEntries(entries)}
        </>
      )}
    </PortalPage>
  );
}

export default AuditLogPage;
