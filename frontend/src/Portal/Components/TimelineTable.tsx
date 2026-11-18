import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import { PortalEvent } from '../PortalTypes';
import styles from '../Portal.css';

const columns: Column[] = [
  { name: 'at', label: () => translate('PortalWhen') },
  { name: 'what', label: () => translate('PortalWhat') },
  { name: 'who', label: () => translate('PortalWho') },
  { name: 'detail', label: () => translate('PortalDetail') },
].map((column) => ({ ...column, isVisible: true }));

interface TimelineTableProps {
  events: PortalEvent[];

  // How long the audit log keeps an entry ("1 year"), when this began before that: its oldest events may be gone.
  trimmedAfter?: string | null;
}

// What happened, oldest first, from Requestarr's audit log.
function TimelineTable({ events, trimmedAfter }: TimelineTableProps) {
  const trimmed = trimmedAfter ? (
    <div className={styles.empty}>
      {translate('PortalHistoryTrimmed', { period: trimmedAfter })}
    </div>
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
