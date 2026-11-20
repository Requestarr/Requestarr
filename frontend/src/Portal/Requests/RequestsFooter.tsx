import React, { useMemo } from 'react';
import translate from 'Utilities/String/translate';
import IndexFooter, { LegendItem } from '../Components/IndexFooter';
import { contentTypeText, statusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { requestStatusKind } from './RequestStatusLabel';

// The colours the status labels use; the footer also counts the requests shown in each.
function legend(): LegendItem[] {
  return [
    { kind: 'warning', label: statusText('pending') },
    { kind: 'primary', label: statusText('approved') },
    { kind: 'info', label: translate('PortalDownloading') },
    { kind: 'success', label: statusText('available') },
    { kind: 'danger', label: translate('PortalDeniedOrFailed') },
    { kind: 'default', label: translate('PortalOtherStatuses') },
  ];
}

const STATUSES_PER_COLUMN = 3;

function countOf(
  requests: PortalRequest[],
  test: (r: PortalRequest) => boolean
) {
  return requests.filter(test).length;
}

interface RequestsFooterProps {
  // The requests the filter shows, as Sonarr's footer counts the series shown.
  requests: PortalRequest[];
}

// The colours the status labels use, and how many requests the filter shows of each type and status.
function RequestsFooter({ requests }: RequestsFooterProps) {
  const statistics = useMemo(() => {
    const typeCount = (type: string) =>
      countOf(requests, (r) => r.contentType === type);
    const statusCounts = legend().map(({ kind, label }) => ({
      title: label,
      data: countOf(requests, (r) => requestStatusKind(r) === kind),
    }));

    return [
      [
        { title: translate('PortalRequests'), data: requests.length },
        { title: contentTypeText('movie'), data: typeCount('movie') },
        { title: contentTypeText('tv'), data: typeCount('tv') },
      ],

      // Three statuses to a column, in the legend's order.
      statusCounts.slice(0, STATUSES_PER_COLUMN),
      statusCounts.slice(STATUSES_PER_COLUMN),
    ];
  }, [requests]);

  return <IndexFooter legend={legend()} statistics={statistics} />;
}

export default RequestsFooter;
