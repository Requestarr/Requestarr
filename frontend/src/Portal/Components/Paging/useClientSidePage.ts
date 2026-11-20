import { useCallback, useMemo, useState } from 'react';

// Servarr's pager (TablePager) over a list the page already has whole, the Requests and Members lists: the page size
// comes from Table Options, as Servarr's server-side lists have it. view says how the list is filtered and sorted;
// when it changes the first page shows, as Servarr's sort and filter handlers do. A list that shrinks under the page
// shown shows its last page.
function useClientSidePage<T>(items: T[], pageSize: number, view: string) {
  const [asked, setAsked] = useState({ view, page: 1 });
  const totalPages = Math.max(1, Math.ceil(items.length / pageSize));
  const page = Math.min(asked.view === view ? asked.page : 1, totalPages);

  const pageItems = useMemo(
    () => items.slice((page - 1) * pageSize, page * pageSize),
    [items, page, pageSize]
  );

  const onPageSelect = useCallback(
    (chosen: number) => setAsked({ view, page: chosen }),
    [view]
  );

  const pager = useMemo(
    () => ({
      page,
      totalPages,
      totalRecords: items.length,
      isFetching: false,
      onFirstPagePress: () => onPageSelect(1),
      onPreviousPagePress: () => onPageSelect(Math.max(1, page - 1)),
      onNextPagePress: () => onPageSelect(Math.min(totalPages, page + 1)),
      onLastPagePress: () => onPageSelect(totalPages),
      onPageSelect,
    }),
    [page, totalPages, items.length, onPageSelect]
  );

  return { pageItems, pager };
}

export default useClientSidePage;
