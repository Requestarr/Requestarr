import React, { ReactNode, useCallback, useEffect } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { AnyAction } from 'redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TablePager from 'Components/Table/TablePager';
import usePaging from 'Components/Table/usePaging';
import { kinds } from 'Helpers/Props';
import { listActions, showList } from 'Store/Actions/portalListActions';
import translate from 'Utilities/String/translate';

// The cells a row shows, in its columns' order, for the columns Table Options shows; the options column's cell is
// empty, as Sonarr's History rows leave room under its cog.
export function visibleCells(
  columns: Column[],
  cells: Record<string, ReactNode>
) {
  return columns
    .filter((column) => column.isVisible)
    .map((column) => (
      <React.Fragment key={column.name}>
        {cells[column.name] ?? <TableRowCell />}
      </React.Fragment>
    ));
}

interface PortalListProps<T> {
  // Its section in the store (Store/Actions/portalListActions.js).
  name: string;

  // What every page is read with: the member's or request's ID, the audit log's search.
  query?: Record<string, unknown>;

  // The heading, shown with how many there are ("Their requests (101)"); a list that is the page's own has none.
  title?: string;
  emptyText: string;

  // Goes up after an action changes the list, so the page shown is read again.
  round?: number;

  // A row: a TableRow with visibleCells.
  renderRow: (item: T, columns: Column[]) => ReactNode;
}

// What each list's section has (Store/Actions/portalListActions.js): its fetch, the pager's pages, sort and Table
// Options.
interface ListActions {
  fetch: () => AnyAction;
  gotoPage: ({ page }: { page: number }) => AnyAction;
  gotoFirstPage: () => AnyAction;
  setSort: (payload: { sortKey: string }) => AnyAction;
  setTableOption: (payload: unknown) => AnyAction;
}

// The list's section in the store, read for its query and again after each action, with what its table and pager
// do: sort, change its columns or page size (back to the first page, as Servarr's LogsTableConnector), and turn pages.
function usePortalList(
  name: string,
  query: Record<string, unknown> | undefined,
  round: number
) {
  const list = useSelector((state: AppState) => state.portalLists[name]);
  const actions = listActions[name] as unknown as ListActions;
  const dispatch = useDispatch();
  const queryKey = JSON.stringify(query ?? {});

  useEffect(() => {
    dispatch(showList(name, JSON.parse(queryKey)));
  }, [name, queryKey, dispatch]);

  useEffect(() => {
    if (round) {
      dispatch(actions.fetch());
    }
  }, [round, actions, dispatch]);

  const paging = usePaging({
    page: list.page,
    totalPages: list.totalPages,
    gotoPage: actions.gotoPage,
  });
  const onSortPress = useCallback(
    (sortKey: string) => dispatch(actions.setSort({ sortKey })),
    [actions, dispatch]
  );
  const onTableOptionChange = useCallback(
    (payload: { pageSize?: number }) => {
      dispatch(actions.setTableOption(payload));

      if (payload.pageSize) {
        dispatch(actions.gotoFirstPage());
      }
    },
    [actions, dispatch]
  );

  return { list, paging, onSortPress, onTableOptionChange };
}

// A list read from the server a page at a time, as Servarr's System, then Events (System/Events/LogsTable) and
// Sonarr's History draw theirs: Servarr's Table, sorted by a column's heading, its columns and page size under the
// table's options, and Servarr's TablePager across the list's full width.
function PortalList<T>(props: PortalListProps<T>) {
  const { name, query, title, emptyText, round = 0, renderRow } = props;
  const { list, paging, onSortPress, onTableOptionChange } = usePortalList(
    name,
    query,
    round
  );
  const {
    handleFirstPagePress: onFirstPagePress,
    handlePreviousPagePress: onPreviousPagePress,
    handleNextPagePress: onNextPagePress,
    handleLastPagePress: onLastPagePress,
    handlePageSelect: onPageSelect,
  } = paging;
  const items = list.items as T[];
  const body = (
    <>
      {list.isFetching && !list.isPopulated && <LoadingIndicator />}

      {!list.isFetching && !!list.error && (
        <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
      )}

      {list.isPopulated && !list.error && !items.length && (
        <Alert kind={kinds.INFO}>{emptyText}</Alert>
      )}

      {list.isPopulated && !list.error && !!items.length && (
        <>
          <Table
            columns={list.columns}
            sortKey={list.sortKey}
            sortDirection={list.sortDirection}
            pageSize={list.pageSize}
            onSortPress={onSortPress}
            onTableOptionChange={onTableOptionChange}
          >
            <TableBody>
              {items.map((item, index) => (
                // eslint-disable-next-line react/no-array-index-key -- a page from the server is shown whole, never reordered on the page
                <React.Fragment key={index}>
                  {renderRow(item, list.columns)}
                </React.Fragment>
              ))}
            </TableBody>
          </Table>

          <TablePager
            page={list.page}
            totalPages={list.totalPages}
            totalRecords={list.totalRecords}
            isFetching={list.isFetching}
            onFirstPagePress={onFirstPagePress}
            onPreviousPagePress={onPreviousPagePress}
            onNextPagePress={onNextPagePress}
            onLastPagePress={onLastPagePress}
            onPageSelect={onPageSelect}
          />
        </>
      )}
    </>
  );

  if (!title) {
    return body;
  }

  return (
    <FieldSet
      legend={list.isPopulated ? `${title} (${list.totalRecords})` : title}
    >
      {body}
    </FieldSet>
  );
}

export default PortalList;
