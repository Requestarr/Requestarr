import React from 'react';
import Label, { LabelProps } from 'Components/Label';
import { kinds } from 'Helpers/Props';
import { requestStatusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';

type LabelKind = NonNullable<LabelProps['kind']>;

function approvedKind(progress: PortalRequest['progress']): LabelKind {
  if (progress === 'downloading') {
    return kinds.INFO;
  }

  return progress === 'stalled' || progress === 'failed'
    ? kinds.DANGER
    : kinds.PRIMARY;
}

// A request's status as a Servarr label, coloured the way the Requests footer's legend says: waiting is orange,
// approved blue, downloading light blue, available green, denied or failed red.
export function requestStatusKind(request: PortalRequest): LabelKind {
  switch (request.status) {
    case 'pending':
      return kinds.WARNING;
    case 'available':
      return kinds.SUCCESS;
    case 'denied':
    case 'failed':
      return kinds.DANGER;
    case 'approved':
      return approvedKind(request.progress);
    default:
      return kinds.DEFAULT;
  }
}

interface RequestStatusLabelProps {
  request: PortalRequest;
}

function RequestStatusLabel({ request }: RequestStatusLabelProps) {
  return (
    <Label kind={requestStatusKind(request)}>
      {requestStatusText(request)}
    </Label>
  );
}

export default RequestStatusLabel;
