import React, { useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import VirtualTableSelectCell from 'Components/Table/Cells/VirtualTableSelectCell';

interface SelectCellProps {
  id: number;
}

interface SelectedChange {
  id: number;
  value: boolean | null;
  shiftKey?: boolean;
}

// Whether the row is picked, and what ticking its box does, kept in the page's SelectProvider, as Sonarr main's
// SeriesIndexRow does it.
function useRowSelect(id: number) {
  const [selectState, selectDispatch] = useSelect();
  const onSelectedChange = useCallback(
    ({ value, shiftKey = false }: SelectedChange) => {
      selectDispatch({
        type: 'toggleSelected',
        id,
        isSelected: !!value,
        shiftKey,
      });
    },
    [id, selectDispatch]
  );

  return { isSelected: !!selectState.selectedState[id], onSelectedChange };
}

// A row's box in select mode, in a table.
function SelectCell({ id }: SelectCellProps) {
  return <TableSelectCell id={id} {...useRowSelect(id)} />;
}

// A row's box in select mode, in a table that draws only the rows on screen.
export function VirtualSelectCell({ id }: SelectCellProps) {
  return (
    <VirtualTableSelectCell id={id} isDisabled={false} {...useRowSelect(id)} />
  );
}

export default SelectCell;
