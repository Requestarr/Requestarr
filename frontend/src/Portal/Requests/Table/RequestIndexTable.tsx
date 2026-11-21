import React, { useCallback, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import { setRequestSort } from 'Store/Actions/requestIndexActions';
import IndexTable from '../../Components/Index/IndexTable';
import { PortalRequest } from '../../PortalTypes';
import { RequestIndexViewProps } from '../RequestIndexViewProps';
import RequestIndexRow from './RequestIndexRow';
import headerStyles from './RequestIndexTableHeader.css';

// A row's height, as Sonarr's table rows: taller with the poster column, as with Sonarr's banners.
const ROW_HEIGHT = 38;
const POSTER_ROW_HEIGHT = 60;

// Every request in the Table view, its columns from Table Options, as Sonarr main's Series/Index/Table/SeriesIndexTable
// lists the series.
function RequestIndexTable(props: RequestIndexViewProps) {
  const { items, isSelectMode } = props;
  const columns = useSelector((state: AppState) => state.requestIndex.columns);
  const dispatch = useDispatch();
  const onSortPress = useCallback(
    (sortKey: string) => dispatch(setRequestSort({ sortKey })),
    [dispatch]
  );
  const hasPosters = columns.some(
    (column) => column.name === 'poster' && column.isVisible
  );
  const texts = useMemo(() => items.map((request) => request.title), [items]);
  const renderRow = useCallback(
    (request: PortalRequest) => (
      <RequestIndexRow
        request={request}
        columns={columns}
        isSelectMode={isSelectMode}
      />
    ),
    [columns, isSelectMode]
  );

  return (
    <IndexTable
      {...props}
      columns={columns}
      columnStyles={headerStyles as unknown as Record<string, string>}
      texts={texts}
      rowHeight={hasPosters ? POSTER_ROW_HEIGHT : ROW_HEIGHT}
      renderRow={renderRow}
      onSortPress={onSortPress}
    />
  );
}

export default RequestIndexTable;
