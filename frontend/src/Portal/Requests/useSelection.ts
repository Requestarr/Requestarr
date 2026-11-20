import { useCallback, useState } from 'react';

const NONE: ReadonlySet<number> = new Set();

// Select mode, as Sonarr's Select Series: on and off from the toolbar (turning it either way clears the picks), the
// boxes ticked, and a whole set at once (Select all, Clear).
function useSelection() {
  const [isSelecting, setIsSelecting] = useState(false);
  const [selected, setSelected] = useState(NONE);

  const onToggle = useCallback(() => {
    setIsSelecting((current) => !current);
    setSelected(NONE);
  }, []);
  const onSelect = useCallback((id: number, isSelected: boolean) => {
    setSelected((current) => {
      const next = new Set(current);

      if (isSelected) {
        next.add(id);
      } else {
        next.delete(id);
      }

      return next;
    });
  }, []);
  const onSelectOnly = useCallback(
    (ids: number[]) => setSelected(new Set(ids)),
    []
  );

  return { isSelecting, selected, onToggle, onSelect, onSelectOnly };
}

export type Selection = ReturnType<typeof useSelection>;

export default useSelection;
