import { useCallback, useState } from 'react';
import { sortDirections } from 'Helpers/Props';
import usePortal from '../../usePortal';
import { PagerProps, usePageSize } from './PortalPager';

interface PagedAnswer<T> {
  page: number;
  pageSize: number;
  totalRecords: number;
  records: T[];
}

// A portal list read from the server a page at a time, sorted there (path: its address, with any filters already in
// it); pressing a column sorts by it, again turns the order round, and goes back to the first page.
function usePagedPortal<T>(
  path: string,
  initialSort?: string,
  initialDirection: string = sortDirections.ASCENDING
) {
  const { pageSize, onPageSizeChange } = usePageSize();
  const [page, setPage] = useState(1);
  const [sortKey, setSortKey] = useState(initialSort);
  const [sortDirection, setSortDirection] = useState<string>(initialDirection);
  const sorting = sortKey
    ? `&sortKey=${sortKey}&sortDirection=${sortDirection}`
    : '';
  const joiner = path.includes('?') ? '&' : '?';
  const result = usePortal<PagedAnswer<T>>(
    `${path}${joiner}page=${page}&pageSize=${pageSize}${sorting}`
  );

  const onSortPress = useCallback(
    (key: string) => {
      const flip =
        key === sortKey && sortDirection === sortDirections.ASCENDING;
      setSortKey(key);
      setSortDirection(
        flip ? sortDirections.DESCENDING : sortDirections.ASCENDING
      );
      setPage(1);
    },
    [sortKey, sortDirection]
  );

  // The page the server answered (it keeps the page within the list when filters shrink it).
  const pager: PagerProps = {
    page: result.data?.page ?? page,
    pageSize,
    totalRecords: result.data?.totalRecords ?? 0,
    isFetching: result.isLoading,
    onPageChange: setPage,
    onPageSizeChange: (size: number) => {
      onPageSizeChange(size);
      setPage(1);
    },
  };

  return {
    records: result.data?.records,
    total: result.data?.totalRecords,
    hasError: result.hasError,
    sort: { sortKey: sortKey ?? '', sortDirection, onSortPress },
    pager,
    reload: result.reload,
  };
}

export default usePagedPortal;
