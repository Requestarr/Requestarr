import { useCallback, useEffect, useState } from 'react';

// Leaving a form with changes not saved asks first: closing it in the app opens Servarr's pending-changes confirm
// (isAsking), and closing or reloading the tab gets the browser's own leave warning. Nothing changed, or saved: it
// just closes.
function useLeaveWarning(hasChanges: boolean, onLeave: () => void) {
  const [isAsking, setIsAsking] = useState(false);

  useEffect(() => {
    if (!hasChanges) {
      return undefined;
    }

    const warn = (event: BeforeUnloadEvent) => {
      event.preventDefault();
      event.returnValue = '';
    };

    window.addEventListener('beforeunload', warn);

    return () => window.removeEventListener('beforeunload', warn);
  }, [hasChanges]);

  const onClose = useCallback(() => {
    if (hasChanges) {
      setIsAsking(true);
      return;
    }

    onLeave();
  }, [hasChanges, onLeave]);

  const onConfirm = useCallback(() => {
    setIsAsking(false);
    onLeave();
  }, [onLeave]);

  const onCancel = useCallback(() => setIsAsking(false), []);

  return { isAsking, onClose, onConfirm, onCancel };
}

export default useLeaveWarning;
