import React from 'react';
import Label, { LabelProps } from 'Components/Label';
import { kinds } from 'Helpers/Props';
import { isImported, memberStatusText } from '../portalText';
import { MemberStatus } from '../PortalTypes';

type LabelKind = NonNullable<LabelProps['kind']>;

// A member's status as a Servarr label, coloured the way the Members footer's legend says: active green, suspended
// orange, removed red, known only from an import grey.
export function memberStatusKind(status: MemberStatus): LabelKind {
  if (isImported(status)) {
    return kinds.DEFAULT;
  }

  switch (status) {
    case 'active':
      return kinds.SUCCESS;
    case 'suspended':
      return kinds.WARNING;
    default:
      return kinds.DANGER;
  }
}

function MemberStatusLabel({ status }: { status: MemberStatus }) {
  return (
    <Label kind={memberStatusKind(status)}>{memberStatusText(status)}</Label>
  );
}

export default MemberStatusLabel;
