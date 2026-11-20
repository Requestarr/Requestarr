import { useCallback, useMemo, useState } from 'react';

// A row's ID: a number, or text for a Discord ID (too long for a number).
export type RowId = number | string;

const NONE: ReadonlySet<RowId> = new Set();

interface Row {
  id: RowId;
}

// The picks with some rows ticked or unticked.
function withRows(
  current: ReadonlySet<RowId>,
  ids: RowId[],
  isSelected: boolean
) {
  const next = new Set(current);

  for (const id of ids) {
    if (isSelected) {
      next.add(id);
    } else {
      next.delete(id);
    }
  }

  return next;
}

// Picking rows, as an email or file list does: a box on every row, a box for the page shown, every row matching the
// filters at once, and Clear. Picks the filters now hide aren't acted on.
function useSelection<T extends Row>(matching: T[], page: T[]) {
  const [selected, setSelected] = useState(NONE);

  const picked = useMemo(
    () => matching.filter((row) => selected.has(row.id)),
    [matching, selected]
  );
  const pickedOnPage = page.filter((row) => selected.has(row.id)).length;

  const onSelect = useCallback(
    (id: RowId, isSelected: boolean) =>
      setSelected((current) => withRows(current, [id], isSelected)),
    []
  );
  const onPageChange = useCallback(
    ({ value }: { value: boolean }) =>
      setSelected((current) =>
        withRows(
          current,
          page.map((row) => row.id),
          value
        )
      ),
    [page]
  );
  const onSelectAllMatching = useCallback(
    () => setSelected(new Set(matching.map((row) => row.id))),
    [matching]
  );
  const onClear = useCallback(() => setSelected(NONE), []);

  return {
    selected,
    picked,
    matchingCount: matching.length,
    allOnPage: page.length > 0 && pickedOnPage === page.length,
    noneOnPage: pickedOnPage === 0,
    onSelect,
    onPageChange,
    onSelectAllMatching,
    onClear,
  };
}

export type Selection = ReturnType<typeof useSelection>;

export default useSelection;
