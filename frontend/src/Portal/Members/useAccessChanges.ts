import { useCallback, useState } from 'react';
import { ContentType } from '../PortalTypes';
import { TypeAccess } from './TypeAccessFields';

type AccessChanges = Partial<Record<ContentType, Partial<TypeAccess>>>;

// What the admin changed of each type's access and limit in a window, kept until it's saved.
function useAccessChanges() {
  const [changes, setChanges] = useState<AccessChanges>({});
  const onAccessChange = useCallback(
    (type: ContentType, changed: Partial<TypeAccess>) =>
      setChanges((current) => ({
        ...current,
        [type]: { ...current[type], ...changed },
      })),
    []
  );

  return { changes, onAccessChange };
}

export default useAccessChanges;
