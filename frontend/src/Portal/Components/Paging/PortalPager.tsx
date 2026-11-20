import React, { useCallback, useState } from 'react';
import SelectInput from 'Components/Form/SelectInput';
import TablePager from 'Components/Table/TablePager';
import translate from 'Utilities/String/translate';
import styles from './PortalPager.css';

// How many a page shows: 25 until a size is picked, then that size in this browser, for every list.
const PAGE_SIZE_KEY = 'requestarr.portal.pageSize';
const DEFAULT_SIZE = 25;
const SIZES = [25, 50, 100, 250];

function storedSize() {
  try {
    const stored = Number(localStorage.getItem(PAGE_SIZE_KEY));

    return SIZES.includes(stored) ? stored : DEFAULT_SIZE;
  } catch {
    return DEFAULT_SIZE;
  }
}

// The page size, remembered in this browser (a browser that keeps nothing uses 25 each time).
export function usePageSize() {
  const [pageSize, setPageSize] = useState(storedSize);
  const onPageSizeChange = useCallback((size: number) => {
    setPageSize(size);

    try {
      localStorage.setItem(PAGE_SIZE_KEY, String(size));
    } catch {
      // Remembered for this visit only.
    }
  }, []);

  return { pageSize, onPageSizeChange };
}

export interface PagerProps {
  page: number;
  pageSize: number;
  totalRecords: number;
  isFetching?: boolean;
  onPageChange: (page: number) => void;
  onPageSizeChange: (size: number) => void;
}

const SIZE_CHOICES = SIZES.map((size) => ({ key: size, value: String(size) }));

// Servarr's pager (first, previous, page X of Y, next, last) and how many a page shows; nothing for a list that fits
// on one page of the smallest size.
function PortalPager(props: PagerProps) {
  const { page, pageSize, totalRecords, isFetching, onPageChange } = props;
  const totalPages = Math.max(1, Math.ceil(totalRecords / pageSize));
  const onFirst = useCallback(() => onPageChange(1), [onPageChange]);
  const onPrevious = useCallback(
    () => onPageChange(Math.max(1, page - 1)),
    [page, onPageChange]
  );
  const onNext = useCallback(
    () => onPageChange(Math.min(totalPages, page + 1)),
    [page, totalPages, onPageChange]
  );
  const onLast = useCallback(
    () => onPageChange(totalPages),
    [totalPages, onPageChange]
  );
  const { onPageSizeChange } = props;
  const onSize = useCallback(
    ({ value }: { value: string }) => onPageSizeChange(Number(value)),
    [onPageSizeChange]
  );

  if (totalRecords <= SIZES[0]) {
    return null;
  }

  return (
    <div className={styles.pager}>
      <TablePager
        page={page}
        totalPages={totalPages}
        totalRecords={totalRecords}
        isFetching={isFetching ?? false}
        onFirstPagePress={onFirst}
        onPreviousPagePress={onPrevious}
        onNextPagePress={onNext}
        onLastPagePress={onLast}
        onPageSelect={onPageChange}
      />
      <label className={styles.size}>
        {translate('PortalPerPage')}
        <SelectInput
          name="pageSize"
          value={pageSize}
          values={SIZE_CHOICES}
          onChange={onSize}
        />
      </label>
    </div>
  );
}

export default PortalPager;
