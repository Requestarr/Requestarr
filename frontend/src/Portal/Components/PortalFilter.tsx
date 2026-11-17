import React, { useCallback } from 'react';
import SelectInput from 'Components/Form/SelectInput';
import translate from 'Utilities/String/translate';
import styles from '../Portal.css';

export const ALL = 'all';

export interface FilterOption {
  key: string;
  value: string;
}

interface PortalFilterProps {
  name: string;
  label: string;
  value: string;
  options: FilterOption[];
  onChange: (name: string, value: string) => void;
}

// One of a list page's filters: a labelled drop-down whose first choice is "All".
function PortalFilter({
  name,
  label,
  value,
  options,
  onChange,
}: PortalFilterProps) {
  const values = [{ key: ALL, value: translate('PortalAll') }, ...options];

  const onSelect = useCallback(
    (change: { name: string; value: string }) =>
      onChange(change.name, change.value),
    [onChange]
  );

  return (
    <label className={styles.filter}>
      <span className={styles.filterLabel}>{label}</span>
      <SelectInput
        name={name}
        value={value}
        values={values}
        onChange={onSelect}
      />
    </label>
  );
}

// The distinct values rows have for a filter, sorted by what they show.
export function optionsOf<T>(
  rows: T[],
  option: (row: T) => FilterOption | null
) {
  const byKey = new Map<string, FilterOption>();

  rows.forEach((row) => {
    const found = option(row);

    if (found) {
      byKey.set(found.key, found);
    }
  });

  return [...byKey.values()].sort((a, b) => a.value.localeCompare(b.value));
}

export default PortalFilter;
