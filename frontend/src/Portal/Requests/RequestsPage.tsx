import React, { useCallback, useMemo, useState } from 'react';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import PortalFilter, { ALL, optionsOf } from '../Components/PortalFilter';
import PortalPage from '../Components/PortalPage';
import RequestsTable, {
  RequestsSort,
  sortValues,
} from '../Components/RequestsTable';
import { contentTypeText, statusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import usePortal from '../usePortal';
import useSortedRows from '../useSortedRows';
import RemoveTitleModal from './RemoveTitleModal';
import RequestsOverview from './RequestsOverview';
import RequestsPosters from './RequestsPosters';
import RequestsToolbar from './RequestsToolbar';
import { RequestView, savedView, saveView } from './requestView';
import styles from '../Portal.css';

const NO_REQUESTS: PortalRequest[] = [];

const DAY_MS = 24 * 60 * 60 * 1000;
const DAY_RANGES = [7, 30, 90, 365];

interface Filters {
  member: string;
  type: string;
  status: string;
  instance: string;
  days: string;
}

const NO_FILTERS: Filters = {
  member: ALL,
  type: ALL,
  status: ALL,
  instance: ALL,
  days: ALL,
};

const LABELS: Record<keyof Filters, string> = {
  member: 'PortalMember',
  type: 'PortalType',
  status: 'PortalStatus',
  instance: 'PortalInstance',
  days: 'PortalRequested',
};

const FILTER_NAMES = Object.keys(LABELS) as (keyof Filters)[];

// since: the earliest request time the "Requested" filter lets through, or null for any time.
function matches(
  request: PortalRequest,
  filters: Filters,
  since: number | null
) {
  const people = [request.requester, ...request.followers].map((p) =>
    String(p.id)
  );
  const isIn = (filter: string, value: string) =>
    filter === ALL || filter === value;

  return (
    (filters.member === ALL || people.includes(filters.member)) &&
    isIn(filters.type, request.contentType) &&
    isIn(filters.status, request.status) &&
    isIn(filters.instance, String(request.instanceId)) &&
    (since === null || Date.parse(request.requested) >= since)
  );
}

// Each filter's choices, from the requests there are: nobody picks a member or an instance with nothing to show.
function filterOptions(requests: PortalRequest[]) {
  return {
    member: optionsOf(
      requests.flatMap((r) => [r.requester, ...r.followers]),
      (p) => ({ key: String(p.id), value: p.name ?? String(p.id) })
    ),
    type: optionsOf(requests, (r) => ({
      key: r.contentType,
      value: contentTypeText(r.contentType),
    })),
    status: optionsOf(requests, (r) => ({
      key: r.status,
      value: statusText(r.status),
    })),
    instance: optionsOf(requests, (r) =>
      r.instanceId == null
        ? null
        : { key: String(r.instanceId), value: r.instance ?? '' }
    ),
    days: DAY_RANGES.map((days) => ({
      key: String(days),
      value: translate('PortalLastDays', { days }),
    })),
  };
}

interface RequestsListProps {
  requests: PortalRequest[];
  view: RequestView;
  sort: RequestsSort;
}

// The requests, already sorted, in the view chosen.
function Shown({ requests, view, sort }: RequestsListProps) {
  if (view === 'overview') {
    return <RequestsOverview requests={requests} />;
  }

  return view === 'posters' ? (
    <RequestsPosters requests={requests} />
  ) : (
    <RequestsTable requests={requests} withPosters={true} sort={sort} />
  );
}

function RequestsList({ requests, view, sort }: RequestsListProps) {
  const [filters, setFilters] = useState(NO_FILTERS);
  // "Requested in the last 30 days" counts from when it was picked.
  const [since, setSince] = useState<number | null>(null);
  const options = useMemo(() => filterOptions(requests), [requests]);
  const shown = useMemo(
    () => requests.filter((r) => matches(r, filters, since)),
    [requests, filters, since]
  );

  const onFilterChange = useCallback((name: string, value: string) => {
    setFilters((current) => ({ ...current, [name]: value }));

    if (name === 'days') {
      setSince(value === ALL ? null : Date.now() - Number(value) * DAY_MS);
    }
  }, []);

  return (
    <>
      <div className={styles.filters}>
        {FILTER_NAMES.map((name) => (
          <PortalFilter
            key={name}
            name={name}
            label={translate(LABELS[name])}
            value={filters[name]}
            options={options[name]}
            onChange={onFilterChange}
          />
        ))}
      </div>
      <div className={styles.count}>
        {translate('PortalShowingRequests', {
          shown: shown.length,
          total: requests.length,
        })}
      </div>
      <Shown requests={shown} view={view} sort={sort} />
    </>
  );
}

// Every request, in the Overview, Posters or Table view, filterable by member (requester or follower), type, status,
// instance and how recent, and sorted the same in every view; Remove a title starts a removal of anything in a library.
function RequestsPage() {
  const result = usePortal<PortalRequest[]>('requests');
  const onActionDone = result.reload;
  const [isRemoving, setIsRemoving] = useState(false);
  const onRemovePress = useCallback(() => setIsRemoving(true), []);
  const onRemoveClose = useCallback(() => setIsRemoving(false), []);
  const [view, setView] = useState(savedView);
  const onViewChange = useCallback((chosen: RequestView) => {
    setView(chosen);
    saveView(chosen);
  }, []);
  const sort = useSortedRows(
    result.data ?? NO_REQUESTS,
    sortValues,
    'requested',
    sortDirections.DESCENDING
  );
  const renderRequests = useCallback(
    () => <RequestsList requests={sort.sorted} view={view} sort={sort} />,
    [sort, view]
  );
  const toolbar = (
    <RequestsToolbar
      view={view}
      sort={sort}
      onViewChange={onViewChange}
      onRemovePress={onRemovePress}
    />
  );

  return (
    <>
      <PortalPage
        title={translate('PortalRequests')}
        result={result}
        toolbar={toolbar}
      >
        {renderRequests}
      </PortalPage>
      <RemoveTitleModal
        isOpen={isRemoving}
        onClose={onRemoveClose}
        onDone={onActionDone}
      />
    </>
  );
}

export default RequestsPage;
