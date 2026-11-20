import React from 'react';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import Column from 'Components/Table/Column';
import { VirtualSelectCell } from '../../Components/Select/SelectCell';
import { PortalRequest } from '../../PortalTypes';
import requestCell from '../requestCell';
import styles from './RequestIndexRow.css';

interface RequestIndexRowProps {
  request: PortalRequest;
  columns: Column[];
  isSelectMode: boolean;
}

// One request in the Table view, as Sonarr main's Series/Index/Table/SeriesIndexRow draws a series: the box in select
// mode, then a cell for each column Table Options shows.
function RequestIndexRow({
  request,
  columns,
  isSelectMode,
}: RequestIndexRowProps) {
  return (
    <>
      {isSelectMode ? <VirtualSelectCell id={request.id} /> : null}

      {columns
        .filter((column) => column.isVisible)
        .map((column) =>
          requestCell(
            column.name,
            request,
            VirtualTableRowCell,
            (styles as unknown as Record<string, string>)[column.name]
          )
        )}
    </>
  );
}

export default RequestIndexRow;
