import React, { useCallback } from 'react';
import CheckInput from 'Components/Form/CheckInput';
import { RowId, Selection } from './useSelection';
import styles from './RowCheckbox.css';

interface RowCheckboxProps {
  id: RowId;
  selection: Selection;
}

// A row's box, always there, as in an email list.
function RowCheckbox({ id, selection }: RowCheckboxProps) {
  const { onSelect } = selection;
  const onChange = useCallback(
    ({ value }: { value: boolean }) => onSelect(id, value),
    [id, onSelect]
  );

  return (
    <CheckInput
      containerClassName={styles.box}
      name={`select-${id}`}
      value={selection.selected.has(id)}
      onChange={onChange}
    />
  );
}

export default RowCheckbox;
