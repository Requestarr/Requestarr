import { useCallback, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';

// A search a dialog runs when the admin presses Search: what they typed, and what the portal found for it.
function useSearch<T>(path: string) {
  const [term, setTerm] = useState('');
  const [found, setFound] = useState<T[]>();
  const [problem, setProblem] = useState<string>();

  const onTermChange = useCallback(
    ({ value }: { value: string }) => setTerm(value),
    []
  );

  const onSearch = useCallback(() => {
    callApi<T[]>(`/portal/${path}?term=${encodeURIComponent(term)}`).then(
      setFound,
      (failure: Error) => setProblem(failure.message)
    );
  }, [path, term]);

  return { term, found, problem, onTermChange, onSearch };
}

export default useSearch;
