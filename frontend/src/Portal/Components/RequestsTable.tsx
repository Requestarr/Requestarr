import React from 'react';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { contentTypeText, requestStatusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import requestCell from '../Requests/requestCell';
import useSortedRows, { SortValues } from '../useSortedRows';
import styles from '../Portal.css';

// The columns a list of requests outside the Requests page shows (a member's page); the Requests page takes its own
// from Table Options.
const COLUMNS: Column[] = [
  { name: 'title', label: () => translate('PortalTitle') },
  { name: 'type', label: () => translate('PortalType') },
  { name: 'status', label: () => translate('PortalStatus') },
  { name: 'requester', label: () => translate('PortalRequester') },
  { name: 'followers', label: () => translate('PortalFollowers') },
  { name: 'instance', label: () => translate('PortalInstance') },
  { name: 'requested', label: () => translate('PortalRequested') },
].map((column) => ({ ...column, isVisible: true, isSortable: true }));

const sortValues: SortValues<PortalRequest> = {
  title: (r) => r.title,
  type: (r) => contentTypeText(r.contentType),
  status: (r) => requestStatusText(r),
  requester: (r) => r.requester.name,
  followers: (r) => r.followers.length,
  instance: (r) => r.instance,
  requested: (r) => r.requested,
};

// A sort kept outside the table (the server, for a paged list), which the table shows and changes when a column is
// pressed.
interface RequestsSort {
  sortKey: string;
  sortDirection: string;
  onSortPress: (key: string) => void;
}

interface RequestsTableProps {
  requests: PortalRequest[];

  // The sort of a list paged on the server (already applied to requests).
  sort?: RequestsSort;

  // False for a member's own requests: every row would name them.
  withRequester?: boolean;
}

// Requests in a table, newest first until a column is pressed: each title opens its request, each name its member.
function RequestsTable({
  requests,
  sort,
  withRequester = true,
}: RequestsTableProps) {
  const own = useSortedRows(
    requests,
    sortValues,
    'requested',
    sortDirections.DESCENDING
  );
  const { sorted, sortKey, sortDirection, onSortPress } = sort
    ? { ...sort, sorted: requests }
    : own;
  const columns = COLUMNS.filter(
    (column) => withRequester || column.name !== 'requester'
  );

  if (!requests.length) {
    return <div className={styles.empty}>{translate('PortalNoRequests')}</div>;
  }

  return (
    <Table
      columns={columns}
      sortKey={sortKey}
      sortDirection={sortDirection}
      onSortPress={onSortPress}
    >
      <TableBody>
        {sorted.map((request) => (
          <TableRow key={request.id}>
            {columns.map((column) =>
              requestCell(column.name, request, TableRowCell)
            )}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default RequestsTable;
