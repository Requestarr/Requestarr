import React, { useCallback, useMemo, useState } from 'react';
import translate from 'Utilities/String/translate';
import PortalFilter, { ALL, optionsOf } from '../Components/PortalFilter';
import PortalPage from '../Components/PortalPage';
import RequestsTable from '../Components/RequestsTable';
import { contentTypeText, statusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Portal.css';

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

function RequestsList({ requests }: { requests: PortalRequest[] }) {
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
      <RequestsTable requests={shown} />
    </>
  );
}

function renderRequests(requests: PortalRequest[]) {
  return <RequestsList requests={requests} />;
}

// Every request, filterable by member (requester or follower), type, status, instance and how recent.
function RequestsPage() {
  const result = usePortal<PortalRequest[]>('requests');

  return (
    <PortalPage title={translate('PortalRequests')} result={result}>
      {renderRequests}
    </PortalPage>
  );
}

export default RequestsPage;
