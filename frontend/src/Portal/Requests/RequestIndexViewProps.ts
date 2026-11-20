import { RefObject } from 'react';
import { PortalRequest } from '../PortalTypes';

// What the Requests page gives each of its views, as Sonarr main's SeriesIndex gives SeriesIndexPosters,
// SeriesIndexOverviews and SeriesIndexTable.
export interface RequestIndexViewProps {
  items: PortalRequest[];
  sortKey: string;
  sortDirection?: string;
  jumpToCharacter?: string;
  scrollerRef: RefObject<HTMLElement>;
  isSelectMode: boolean;
  isSmallScreen: boolean;
}
