import React from 'react';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import {
  contentTypeText,
  requestStatusText,
  seasonsText,
  titleText,
} from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { RequestPoster } from '../Requests/RequestMedia';
import useSortedRows, { SortValues } from '../useSortedRows';
import MemberLink from './MemberLink';
import styles from '../Portal.css';
import viewStyles from '../Requests/RequestViews.css';

const columns: Column[] = [
  { name: 'title', label: () => translate('PortalTitle') },
  { name: 'type', label: () => translate('PortalType') },
  { name: 'status', label: () => translate('PortalStatus') },
  { name: 'requester', label: () => translate('PortalRequester') },
  { name: 'followers', label: () => translate('PortalFollowers') },
  { name: 'instance', label: () => translate('PortalInstance') },
  { name: 'requested', label: () => translate('PortalRequested') },
].map((column) => ({ ...column, isVisible: true, isSortable: true }));

const posterColumn: Column = {
  name: 'poster',
  label: '',
  isVisible: true,
  isSortable: false,
};

export const sortValues: SortValues<PortalRequest> = {
  title: (r) => r.title,
  type: (r) => contentTypeText(r.contentType),
  status: (r) => requestStatusText(r),
  requester: (r) => r.requester.name,
  followers: (r) => r.followers.length,
  instance: (r) => r.instance,
  requested: (r) => r.requested,
};

interface RequestRowProps {
  request: PortalRequest;
  withPoster: boolean;
}

function RequestRow({ request, withPoster }: RequestRowProps) {
  const seasons = seasonsText(request.seasons);

  return (
    <TableRow>
      {withPoster && (
        <TableRowCell>
          <RequestPoster
            url={request.media?.poster}
            className={viewStyles.tablePoster}
          />
        </TableRowCell>
      )}
      <TableRowCell>
        <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
        {seasons && <div className={styles.muted}>{seasons}</div>}
      </TableRowCell>
      <TableRowCell>{contentTypeText(request.contentType)}</TableRowCell>
      <TableRowCell>{requestStatusText(request)}</TableRowCell>
      <TableRowCell>
        <MemberLink person={request.requester} />
      </TableRowCell>
      <TableRowCell>
        {request.followers.map((follower, index) => (
          <span key={follower.id}>
            {index > 0 && ', '}
            <MemberLink person={follower} />
          </span>
        ))}
      </TableRowCell>
      <TableRowCell>{request.instance ?? ''}</TableRowCell>
      <RelativeDateCell date={request.requested} />
    </TableRow>
  );
}

export type RequestsSort = ReturnType<typeof useSortedRows<PortalRequest>>;

interface RequestsTableProps {
  requests: PortalRequest[];
  withPosters?: boolean;

  // The sorting of a page that also shows the requests other ways (already applied to requests).
  sort?: RequestsSort;
}

// Requests in a table, newest first until a column is pressed: each title opens its request, each name its member.
function RequestsTable({ requests, withPosters, sort }: RequestsTableProps) {
  const own = useSortedRows(
    requests,
    sortValues,
    'requested',
    sortDirections.DESCENDING
  );
  const { sorted, sortKey, sortDirection, onSortPress } = sort
    ? { ...sort, sorted: requests }
    : own;

  if (!requests.length) {
    return <div className={styles.empty}>{translate('PortalNoRequests')}</div>;
  }

  return (
    <Table
      columns={withPosters ? [posterColumn, ...columns] : columns}
      sortKey={sortKey}
      sortDirection={sortDirection}
      onSortPress={onSortPress}
    >
      <TableBody>
        {sorted.map((request) => (
          <RequestRow
            key={request.id}
            request={request}
            withPoster={withPosters ?? false}
          />
        ))}
      </TableBody>
    </Table>
  );
}

export default RequestsTable;
