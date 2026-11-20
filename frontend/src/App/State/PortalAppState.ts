import Column from 'Components/Table/Column';
import SortDirection from 'Helpers/Props/SortDirection';
import { PortalMember, PortalRequest } from 'Portal/PortalTypes';
import AppSectionState from './AppSectionState';
import { Filter, FilterBuilderProp } from './AppState';

// The Requests and Members lists' store sections, as Prowlarr's IndexerAppState has its indexers and indexerIndex.

interface IndexAppState<T> {
  sortKey: string;
  sortDirection: SortDirection;
  secondarySortKey: string;
  secondarySortDirection: SortDirection;
  pageSize: number;
  selectedFilterKey: string;
  filterBuilderProps: FilterBuilderProp<T>[];
  filters: Filter[];
  columns: Column[];
}

export interface RequestIndexAppState extends IndexAppState<PortalRequest> {
  view: string;
}

export type MemberIndexAppState = IndexAppState<PortalMember>;

export type RequestsAppState = AppSectionState<PortalRequest>;

export type MembersAppState = AppSectionState<PortalMember>;
