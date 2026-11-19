import { useCallback, useMemo, useState } from 'react';
import { sortDirections } from 'Helpers/Props';

type SortValue = string | number | null | undefined;

export type SortValues<T> = Record<string, (row: T) => SortValue>;

function compare(a: SortValue, b: SortValue) {
  if (a == null || b == null) {
    // Rows without a value go last, whichever way the column is sorted.
    return Number(a == null) - Number(b == null);
  }

  if (typeof a === 'number' && typeof b === 'number') {
    return a - b;
  }

  return String(a).localeCompare(String(b), undefined, { sensitivity: 'base' });
}

// A table's rows sorted by the column last pressed; pressing it again turns the order round.
function useSortedRows<T>(
  rows: T[],
  values: SortValues<T>,
  initialKey: string,
  initialDirection = sortDirections.ASCENDING
) {
  const [sortKey, setSortKey] = useState(initialKey);
  const [sortDirection, setSortDirection] = useState(initialDirection);

  const onSortPress = useCallback(
    (key: string) => {
      const flip =
        key === sortKey && sortDirection === sortDirections.ASCENDING;
      setSortKey(key);
      setSortDirection(
        flip ? sortDirections.DESCENDING : sortDirections.ASCENDING
      );
    },
    [sortKey, sortDirection]
  );

  const sorted = useMemo(() => {
    const value = values[sortKey] as ((row: T) => SortValue) | undefined;
    const sign = sortDirection === sortDirections.ASCENDING ? 1 : -1;

    // The sign turns values round, never the rows without one: those stay last.
    const byValue = (a: T, b: T) => {
      const left = value?.(a);
      const right = value?.(b);

      return left == null || right == null
        ? compare(left, right)
        : sign * compare(left, right);
    };

    return value ? [...rows].sort(byValue) : rows;
  }, [rows, values, sortKey, sortDirection]);

  return { sorted, sortKey, sortDirection, onSortPress };
}

export default useSortedRows;
