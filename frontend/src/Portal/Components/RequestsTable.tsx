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
import { PortalPerson, PortalRequest } from '../PortalTypes';
import useSortedRows, { SortValues } from '../useSortedRows';
import styles from '../Portal.css';

const columns: Column[] = [
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

function MemberLink({ person }: { person: PortalPerson }) {
  return (
    <Link to={`/members/${person.id}`}>
      {person.name ?? translate('PortalUnknownMember')}
    </Link>
  );
}

function RequestRow({ request }: { request: PortalRequest }) {
  const seasons = seasonsText(request.seasons);

  return (
    <TableRow>
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

// Requests in a table, newest first until a column is pressed: each title opens its request, each name its member.
function RequestsTable({ requests }: { requests: PortalRequest[] }) {
  const { sorted, sortKey, sortDirection, onSortPress } = useSortedRows(
    requests,
    sortValues,
    'requested',
    sortDirections.DESCENDING
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
          <RequestRow key={request.id} request={request} />
        ))}
      </TableBody>
    </Table>
  );
}

export default RequestsTable;
