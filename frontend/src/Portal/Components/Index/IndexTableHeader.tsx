import React, { useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import Column from 'Components/Table/Column';
import VirtualTableHeader from 'Components/Table/VirtualTableHeader';
import VirtualTableHeaderCell from 'Components/Table/VirtualTableHeaderCell';
import VirtualTableSelectAllHeaderCell from 'Components/Table/VirtualTableSelectAllHeaderCell';
import { CheckInputChanged } from 'typings/inputs';

interface IndexTableHeaderProps {
  columns: Column[];

  // Each column's width, by its name (the same as its cells').
  columnStyles: Record<string, string>;
  sortKey?: string;
  sortDirection?: string;
  isSelectMode: boolean;
  onSortPress: (sortKey: string) => void;
}

// A list's table header, as Sonarr main's Series/Index/Table/SeriesIndexTableHeader and Prowlarr's
// IndexerIndexTableHeader: the box that picks everything the filter shows in select mode, then each column, pressed
// to sort by it. A column with no label (a picture column) has a blank heading and keeps its name in Table Options.
function IndexTableHeader(props: IndexTableHeaderProps) {
  const { columns, columnStyles, sortKey, sortDirection, isSelectMode } = props;
  const [selectState, selectDispatch] = useSelect();

  const onSelectAllChange = useCallback(
    ({ value }: CheckInputChanged) =>
      selectDispatch({ type: value ? 'selectAll' : 'unselectAll' }),
    [selectDispatch]
  );

  return (
    <VirtualTableHeader>
      {isSelectMode ? (
        <VirtualTableSelectAllHeaderCell
          allSelected={selectState.allSelected}
          allUnselected={selectState.allUnselected}
          onSelectAllChange={onSelectAllChange}
        />
      ) : null}

      {columns.map(({ name, label, isSortable, isVisible }) =>
        isVisible ? (
          <VirtualTableHeaderCell
            key={name}
            className={columnStyles[name]}
            name={name}
            sortKey={sortKey}
            sortDirection={sortDirection}
            isSortable={isSortable}
            onSortPress={props.onSortPress}
          >
            {typeof label === 'function' ? label() : label}
          </VirtualTableHeaderCell>
        ) : null
      )}
    </VirtualTableHeader>
  );
}

export default IndexTableHeader;
