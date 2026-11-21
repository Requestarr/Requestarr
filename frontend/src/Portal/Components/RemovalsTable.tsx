import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { removalColumns } from 'Store/Actions/portalListActions';
import { removalStatusText, seasonsText } from '../portalText';
import { PortalRemoval } from '../PortalTypes';
import { visibleCells } from './PortalList';

interface RemovalRowProps {
  removal: PortalRemoval;
  columns: Column[];
}

// A removal: started by a vote or an admin, who pressed Keep it on its vote, and how it ended (or that it's still
// open), in the columns shown.
export function RemovalRow({ removal, columns }: RemovalRowProps) {
  return (
    <TableRow>
      {visibleCells(columns, {
        created: <RelativeDateCell date={removal.created} />,
        status: (
          <TableRowCell>{removalStatusText(removal.status)}</TableRowCell>
        ),
        seasons: <TableRowCell>{seasonsText(removal.seasons)}</TableRowCell>,
        startedBy: <TableRowCell>{removal.startedBy ?? ''}</TableRowCell>,
        keeps: <TableRowCell>{removal.keeps.join(', ')}</TableRowCell>,
        closed: <RelativeDateCell date={removal.closed} />,
        closedBy: <TableRowCell>{removal.closedBy ?? ''}</TableRowCell>,
      })}
    </TableRow>
  );
}

// The store's removal columns (columnLabel there is a function, as Servarr's column definitions have it).
const columns = removalColumns as unknown as Column[];

// A request's latest removal, on its own, with every column.
function RemovalsTable({ removal }: { removal: PortalRemoval }) {
  return (
    <Table columns={columns}>
      <TableBody>
        <RemovalRow removal={removal} columns={columns} />
      </TableBody>
    </Table>
  );
}

export default RemovalsTable;
