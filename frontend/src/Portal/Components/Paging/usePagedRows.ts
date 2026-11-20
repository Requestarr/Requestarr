import { useMemo, useState } from 'react';
import { PagerProps, usePageSize } from './PortalPager';

// A list already in the page, shown a page at a time (the Requests views, the Members list): filters, sorting, the
// count and picking work on all of it, only the rows drawn are the page's. A list that shrinks under the page shown
// shows its last page.
function usePagedRows<T>(rows: T[]) {
  const { pageSize, onPageSizeChange } = usePageSize();
  const [asked, setAsked] = useState(1);
  const pages = Math.max(1, Math.ceil(rows.length / pageSize));
  const page = Math.min(asked, pages);
  const pageRows = useMemo(
    () => rows.slice((page - 1) * pageSize, page * pageSize),
    [rows, page, pageSize]
  );

  const pager: PagerProps = {
    page,
    pageSize,
    totalRecords: rows.length,
    onPageChange: setAsked,
    onPageSizeChange: (size: number) => {
      onPageSizeChange(size);
      setAsked(1);
    },
  };

  return { pageRows, pager };
}

export default usePagedRows;
