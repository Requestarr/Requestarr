import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import { PortalEvent } from '../PortalTypes';
import { visibleCells } from './PortalList';
import styles from '../Portal.css';

const columns: Column[] = [
  { name: 'at', label: () => translate('PortalWhen') },
  { name: 'what', label: () => translate('PortalWhat') },
  { name: 'who', label: () => translate('PortalWho') },
  { name: 'detail', label: () => translate('PortalDetail') },
].map((column) => ({ ...column, isVisible: true }));

interface TimelineTableProps {
  events: PortalEvent[];

  // How long the audit log keeps an entry now ("1 year", or "forever"), when a trim removed this one's oldest events.
  trimmedAfter?: string | null;
}

// That the audit log no longer keeps this list's oldest events, and for how long it keeps them now.
export function TrimmedNote({
  trimmedAfter,
}: {
  trimmedAfter?: string | null;
}) {
  if (!trimmedAfter) {
    return null;
  }

  return (
    <div className={styles.empty}>
      {trimmedAfter === 'forever'
        ? translate('PortalHistoryTrimmedEarlier')
        : translate('PortalHistoryTrimmed', { period: trimmedAfter })}
    </div>
  );
}

// One event in a list read a page at a time, in the columns Table Options shows.
export function EventRow({
  event,
  columns,
}: {
  event: PortalEvent;
  columns: Column[];
}) {
  return (
    <TableRow>
      {visibleCells(columns, {
        at: <RelativeDateCell date={event.at} />,
        what: <TableRowCell>{event.what}</TableRowCell>,
        who: <TableRowCell>{event.who}</TableRowCell>,
        detail: <TableRowCell>{event.detail ?? ''}</TableRowCell>,
      })}
    </TableRow>
  );
}

// What happened, oldest first, from Requestarr's audit log.
function TimelineTable({ events, trimmedAfter }: TimelineTableProps) {
  const trimmed = trimmedAfter ? (
    <TrimmedNote trimmedAfter={trimmedAfter} />
  ) : null;

  if (!events.length) {
    return (
      trimmed ?? (
        <div className={styles.empty}>{translate('PortalNoHistory')}</div>
      )
    );
  }

  return (
    <>
      {trimmed}
      <Table columns={columns}>
        <TableBody>
          {events.map((event, index) => (
            // Two events can share a time and words, so the row's place keeps it apart.
            // eslint-disable-next-line react/no-array-index-key -- the list is never reordered on the page
            <TableRow key={index}>
              <RelativeDateCell date={event.at} />
              <TableRowCell>{event.what}</TableRowCell>
              <TableRowCell>{event.who}</TableRowCell>
              <TableRowCell>{event.detail ?? ''}</TableRowCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </>
  );
}

export default TimelineTable;
