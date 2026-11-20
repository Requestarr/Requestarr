import React, { ReactNode, useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
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
import { OpenInLink, RequestPoster } from '../Requests/RequestMedia';
import { RowActions } from '../Requests/RequestRows';
import RequestStatusLabel from '../Requests/RequestStatusLabel';
import useSortedRows, { SortValues } from '../useSortedRows';
import FollowerStack from './FollowerStack';
import MemberLink from './MemberLink';
import SelectCell from './Select/SelectCell';
import styles from '../Portal.css';
import viewStyles from '../Requests/RequestViews.css';

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

function TitleCell({ request }: { request: PortalRequest }) {
  const seasons = seasonsText(request.seasons);

  return (
    <TableRowCell>
      <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
      {seasons && <div className={styles.muted}>{seasons}</div>}
      <OpenInLink media={request.media} />
    </TableRowCell>
  );
}

// One cell of a request's row, by its column's name, as Sonarr main's SeriesIndexRow draws them.
function requestCell(name: string, request: PortalRequest): ReactNode {
  switch (name) {
    case 'poster':
      return (
        <TableRowCell key={name}>
          <RequestPoster
            url={request.media?.poster}
            className={viewStyles.tablePoster}
          />
        </TableRowCell>
      );
    case 'title':
      return <TitleCell key={name} request={request} />;
    case 'type':
      return (
        <TableRowCell key={name}>
          {contentTypeText(request.contentType)}
        </TableRowCell>
      );
    case 'status':
      return (
        <TableRowCell key={name}>
          <RequestStatusLabel request={request} />
        </TableRowCell>
      );
    case 'requester':
      return (
        <TableRowCell key={name}>
          <MemberLink person={request.requester} size="small" />
        </TableRowCell>
      );
    case 'followers':
      return (
        <TableRowCell key={name}>
          <FollowerStack requestId={request.id} followers={request.followers} />
        </TableRowCell>
      );
    case 'instance':
      return <TableRowCell key={name}>{request.instance ?? ''}</TableRowCell>;
    case 'actions':
      return (
        <TableRowCell key={name}>
          <RowActions request={request} />
        </TableRowCell>
      );
    default:
      return (
        <RelativeDateCell
          key={name}
          date={request[name as 'requested' | 'decided' | 'fulfilled'] ?? ''}
        />
      );
  }
}

interface RequestRowProps {
  request: PortalRequest;
  columns: Column[];
  isSelectMode: boolean;
}

function RequestRow({ request, columns, isSelectMode }: RequestRowProps) {
  return (
    <TableRow>
      {isSelectMode && <SelectCell id={request.id} />}
      {columns
        .filter((column) => column.isVisible)
        .map((column) => requestCell(column.name, request))}
    </TableRow>
  );
}

// The Requests page's header box: picks or clears every request the filter shows, as Sonarr main's
// SeriesIndexTableHeader does.
function useSelectAll() {
  const [selectState, selectDispatch] = useSelect();
  const onSelectAllChange = useCallback(
    ({ value }: { value: boolean }) =>
      selectDispatch({ type: value ? 'selectAll' : 'unselectAll' }),
    [selectDispatch]
  );

  return { ...selectState, onSelectAllChange };
}

// How a table outside the page's own sorting is sorted: the column pressed.
interface RequestsSort {
  sortKey: string;
  sortDirection: string;
  onSortPress: (key: string) => void;
}

interface SelectingTableProps {
  requests: PortalRequest[];
  columns: Column[];
  sort: RequestsSort;
  onTableOptionChange: (payload: unknown) => void;
}

// The Requests page's table: its columns from Table Options, its rows already sorted and filtered, and in select mode
// a box on each row and one in the header.
export function RequestsIndexTable(
  props: SelectingTableProps & { isSelectMode: boolean }
) {
  const { requests, columns, sort, isSelectMode, onTableOptionChange } = props;
  const select = useSelectAll();

  return (
    <Table
      selectAll={isSelectMode}
      allSelected={select.allSelected}
      allUnselected={select.allUnselected}
      columns={columns}
      sortKey={sort.sortKey}
      sortDirection={sort.sortDirection}
      onSelectAllChange={select.onSelectAllChange}
      onSortPress={sort.onSortPress}
      onTableOptionChange={onTableOptionChange}
    >
      <TableBody>
        {requests.map((request) => (
          <RequestRow
            key={request.id}
            request={request}
            columns={columns}
            isSelectMode={isSelectMode}
          />
        ))}
      </TableBody>
    </Table>
  );
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
          <RequestRow
            key={request.id}
            request={request}
            columns={columns}
            isSelectMode={false}
          />
        ))}
      </TableBody>
    </Table>
  );
}

export default RequestsTable;
