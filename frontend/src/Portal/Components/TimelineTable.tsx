import React, { useCallback, useState } from 'react';
import IconButton from 'Components/Link/IconButton';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { PortalEvent } from '../PortalTypes';
import EventDetailsModal from './EventDetailsModal';
import { visibleCells } from './PortalList';
import styles from '../Portal.css';

const columns: Column[] = [
  { name: 'at', label: () => translate('PortalWhen') },
  { name: 'what', label: () => translate('PortalWhat') },
  { name: 'who', label: () => translate('PortalWho') },
  { name: 'detail', label: () => translate('PortalDetail') },
].map((column) => ({ ...column, isVisible: true }));

interface TrimmedNoteProps {
  // How long the audit log keeps an entry now ("1 year", or "forever"), when a trim removed this list's oldest events.
  trimmedAfter?: string | null;
}

// That the audit log no longer keeps this list's oldest events, and for how long it keeps them now.
export function TrimmedNote({ trimmedAfter }: TrimmedNoteProps) {
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
  const [isDetailsOpen, setIsDetailsOpen] = useState(false);
  const onDetailsPress = useCallback(() => setIsDetailsOpen(true), []);
  const onDetailsClose = useCallback(() => setIsDetailsOpen(false), []);

  return (
    <TableRow>
      {visibleCells(columns, {
        at: <RelativeDateCell date={event.at} />,
        what: <TableRowCell>{event.what}</TableRowCell>,
        who: <TableRowCell>{event.who}</TableRowCell>,
        detail: <TableRowCell>{event.detail ?? ''}</TableRowCell>,
        // Under the options cog, as Sonarr's History rows have their details button.
        details: (
          <TableRowCell>
            <IconButton name={icons.INFO} onPress={onDetailsPress} />
            <EventDetailsModal
              event={event}
              isOpen={isDetailsOpen}
              onModalClose={onDetailsClose}
            />
          </TableRowCell>
        ),
      })}
    </TableRow>
  );
}

// What happened, oldest first, from Requestarr's audit log.
function TimelineTable({ events }: { events: PortalEvent[] }) {
  if (!events.length) {
    return <div className={styles.empty}>{translate('PortalNoHistory')}</div>;
  }

  return (
    <Table columns={columns}>
      <TableBody>
        {events.map((event, index) => (
          // Two events can share a time and words, so the row's place keeps it apart.
          // eslint-disable-next-line react/no-array-index-key -- the list is never reordered on the page
          <EventRow key={index} event={event} columns={columns} />
        ))}
      </TableBody>
    </Table>
  );
}

export default TimelineTable;
