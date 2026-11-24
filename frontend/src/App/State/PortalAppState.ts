import Column from 'Components/Table/Column';
import SortDirection from 'Helpers/Props/SortDirection';
import {
  PortalMember,
  PortalRequest,
  PortalServerMember,
} from 'Portal/PortalTypes';
import AppSectionState, {
  PagedAppSectionState,
  TableAppSectionState,
} from './AppSectionState';
import { Filter, FilterBuilderProp } from './AppState';

// The Requests and Members lists' store sections, as Prowlarr's IndexerAppState has its indexers and indexerIndex.

interface IndexAppState<T> {
  sortKey: string;
  sortDirection: SortDirection;
  secondarySortKey: string;
  secondarySortDirection: SortDirection;
  selectedFilterKey: string;
  filterBuilderProps: FilterBuilderProp<T>[];
  filters: Filter[];
  columns: Column[];
}

// What Sonarr main's posterOptions and overviewOptions hold, for requests.
export interface RequestPosterOptions {
  detailedProgressBar: boolean;
  size: string;
  showTitle: boolean;
  showRequester: boolean;
  showFollowers: boolean;
}

export interface RequestOverviewOptions {
  detailedProgressBar: boolean;
  size: string;
  showType: boolean;
  showRequester: boolean;
  showFollowers: boolean;
  showRequested: boolean;
  showInstance: boolean;
  showReleases: boolean;
}

export interface RequestIndexAppState extends IndexAppState<PortalRequest> {
  view: string;
  posterOptions: RequestPosterOptions;
  overviewOptions: RequestOverviewOptions;
}

export type MemberIndexAppState = IndexAppState<PortalMember>;

export type RequestsAppState = AppSectionState<PortalRequest>;

export type MembersAppState = AppSectionState<PortalMember>;

// One section for the people and how they're listed, as Prowlarr's releases.
export type ServerMembersAppState = AppSectionState<PortalServerMember> &
  IndexAppState<PortalServerMember>;

// One of the portal's lists read a page at a time (Store/Actions/portalListActions.js), as Servarr's system.logs.
interface PortalListAppState
  extends AppSectionState<unknown>,
    PagedAppSectionState,
    TableAppSectionState {
  sortKey: string;
  sortDirection: SortDirection;
  query: Record<string, unknown>;
}

export type PortalListsAppState = Record<string, PortalListAppState>;
