import { useCallback, useState } from 'react';

// The page a list is on, back to the first whenever key (how the list is filtered and sorted) changes.
function useKeyedPage(key: string) {
  const [asked, setAsked] = useState({ key, page: 1 });
  const page = asked.key === key ? asked.page : 1;
  const setPage = useCallback(
    (next: number) => setAsked({ key, page: next }),
    [key]
  );

  return { page, setPage };
}

export default useKeyedPage;
