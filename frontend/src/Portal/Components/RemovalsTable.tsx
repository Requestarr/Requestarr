import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import { removalStatusText, seasonsText } from '../portalText';
import { PortalRemoval } from '../PortalTypes';
import styles from '../Portal.css';

const columns: Column[] = [
  { name: 'created', label: () => translate('PortalStarted') },
  { name: 'status', label: () => translate('PortalStatus') },
  { name: 'seasons', label: () => translate('PortalSeasons') },
  { name: 'startedBy', label: () => translate('PortalStartedBy') },
  { name: 'keeps', label: () => translate('PortalKeeps') },
  { name: 'closed', label: () => translate('PortalClosed') },
  { name: 'closedBy', label: () => translate('PortalClosedBy') },
].map((column) => ({ ...column, isVisible: true }));

// Removals: started by a vote or an admin, who pressed Keep it on its vote, and how each ended (or that it's still
// open).
function RemovalsTable({ removals }: { removals: PortalRemoval[] }) {
  if (!removals.length) {
    return <div className={styles.empty}>{translate('PortalNoRemovals')}</div>;
  }

  return (
    <Table columns={columns}>
      <TableBody>
        {removals.map((removal) => (
          <TableRow key={removal.id}>
            <RelativeDateCell date={removal.created} />
            <TableRowCell>{removalStatusText(removal.status)}</TableRowCell>
            <TableRowCell>{seasonsText(removal.seasons)}</TableRowCell>
            <TableRowCell>{removal.startedBy ?? ''}</TableRowCell>
            <TableRowCell>{removal.keeps.join(', ')}</TableRowCell>
            <RelativeDateCell date={removal.closed} />
            <TableRowCell>{removal.closedBy ?? ''}</TableRowCell>
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default RemovalsTable;
