import { useCallback, useEffect, useState } from 'react';
import { callRequestarrApi } from './requestarrApi';

// One of Requestarr's settings answers, fetched when the page opens; reload fetches it again after a change.
function useRequestarrApi<T>(path: string) {
  const [data, setData] = useState<T>();
  const [error, setError] = useState<string>();

  const reload = useCallback(() => {
    callRequestarrApi<T>(path).then(setData, (failure: Error) =>
      setError(failure.message)
    );
  }, [path]);

  useEffect(reload, [reload]);

  return { data, error, reload, setData };
}

export default useRequestarrApi;
