import React, { useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';

interface SelectCellProps {
  id: number;
}

interface SelectedChange {
  id: number;
  value: boolean | null;
  shiftKey?: boolean;
}

// A row's box in select mode, kept in the page's SelectProvider, as Sonarr main's SeriesIndexRow does it.
function SelectCell({ id }: SelectCellProps) {
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

  return (
    <TableSelectCell
      id={id}
      isSelected={!!selectState.selectedState[id]}
      onSelectedChange={onSelectedChange}
    />
  );
}

export default SelectCell;
