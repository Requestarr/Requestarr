import React, { useMemo } from 'react';
import translate from 'Utilities/String/translate';
import IndexFooter, { LegendItem } from '../Components/IndexFooter';
import { contentTypeText, statusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { requestStatusKind } from './RequestStatusLabel';

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
    const kindCount = (kind: string) =>
      countOf(requests, (r) => requestStatusKind(r) === kind);
    const typeCount = (type: string) =>
      countOf(requests, (r) => r.contentType === type);

    return [
      [
        { title: translate('PortalRequests'), data: requests.length },
        { title: contentTypeText('movie'), data: typeCount('movie') },
        { title: contentTypeText('tv'), data: typeCount('tv') },
      ],
      [
        { title: statusText('pending'), data: kindCount('warning') },
        { title: statusText('approved'), data: kindCount('primary') },
        {
          title: translate('PortalDownloading'),
          data: kindCount('info'),
        },
      ],
      [
        { title: statusText('available'), data: kindCount('success') },
        { title: translate('PortalDeniedOrFailed'), data: kindCount('danger') },
        {
          title: translate('PortalOtherStatuses'),
          data: kindCount('default'),
        },
      ],
    ];
  }, [requests]);

  return <IndexFooter legend={legend()} statistics={statistics} />;
}

export default RequestsFooter;
