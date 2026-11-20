import React, { useCallback, useState } from 'react';
import TextInput from 'Components/Form/TextInput';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import PortalList, { visibleCells } from '../Components/PortalList';
import { PortalAuditEntry } from '../PortalTypes';
import styles from '../Portal.css';

const FILTERS = ['member', 'title', 'action', 'from', 'to'] as const;

type Filters = Record<(typeof FILTERS)[number], string>;

// From and To are the admin's own days: from the start of From to the end of To where they are, as moments.
function asSent(filters: Filters): Filters {
  const midnight = (day: string, plusDays: number) => {
    const date = new Date(`${day}T00:00`);
    date.setDate(date.getDate() + plusDays);
    return date.toISOString();
  };

  return {
    ...filters,
    from: filters.from && midnight(filters.from, 0),
    to: filters.to && midnight(filters.to, 1),
  };
}

const NO_FILTERS: Filters = {
  member: '',
  title: '',
  action: '',
  from: '',
  to: '',
};

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

function entryRow(entry: PortalAuditEntry, columns: Column[]) {
  return (
    <TableRow>
      {visibleCells(columns, {
        at: <RelativeDateCell date={entry.at} />,
        what: <TableRowCell>{entry.what}</TableRowCell>,
        who: <TableRowCell>{entry.who}</TableRowCell>,
        detail: <TableRowCell>{entry.detail ?? ''}</TableRowCell>,
        about: (
          <TableRowCell>
            <About entry={entry} />
          </TableRowCell>
        ),
      })}
    </TableRow>
  );
}

// Everything Requestarr recorded, newest first, a page at a time: find it by member, title, action and day.
function AuditLogPage() {
  const [typed, setTyped] = useState(NO_FILTERS);
  const [applied, setApplied] = useState(NO_FILTERS);
  const query = Object.fromEntries(
    Object.entries(asSent(applied)).filter(([, value]) => value)
  );

  const onChange = useCallback(
    ({ name, value }: { name: string; value: string }) => {
      setTyped((current) => ({ ...current, [name]: value }));
    },
    []
  );
  const onSearchPress = useCallback(() => setApplied(typed), [typed]);

  return (
    <PageContent title={translate('PortalAuditLog')}>
      <PageContentBody>
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
        <PortalList
          name="audit"
          query={query}
          emptyText={translate('PortalNothingFound')}
          renderRow={entryRow}
        />
      </PageContentBody>
    </PageContent>
  );
}

export default AuditLogPage;
