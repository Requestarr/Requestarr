import React, { ReactNode } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import PortalPager from './PortalPager';
import usePagedPortal from './usePagedPortal';

export type PagedSort = ReturnType<typeof usePagedPortal>['sort'];

interface PagedSectionProps<T> {
  // The heading, shown with how many there are: "Their requests (101)".
  title: string;
  path: string;
  initialSort?: string;
  initialDirection?: string;
  children: (records: T[], sort: PagedSort) => ReactNode;
}

// One section of a page read a page at a time from the server, its total in its heading and the pager under it.
function PagedSection<T>({
  title,
  path,
  initialSort,
  initialDirection,
  children,
}: PagedSectionProps<T>) {
  const paged = usePagedPortal<T>(path, initialSort, initialDirection);
  const legend =
    paged.total === undefined ? title : `${title} (${paged.total})`;

  return (
    <FieldSet legend={legend}>
      {paged.hasError && (
        <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
      )}
      {paged.records && children(paged.records, paged.sort)}
      <PortalPager {...paged.pager} />
    </FieldSet>
  );
}

export default PagedSection;
