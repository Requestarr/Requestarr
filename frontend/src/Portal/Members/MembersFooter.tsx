import React, { useMemo } from 'react';
import translate from 'Utilities/String/translate';
import IndexFooter, { LegendItem } from '../Components/IndexFooter';
import { memberStatusText } from '../portalText';
import { PortalMember } from '../PortalTypes';
import { memberStatusKind } from './MemberStatusLabel';

function legend(): LegendItem[] {
  return [
    { kind: 'success', label: memberStatusText('active') },
    { kind: 'warning', label: memberStatusText('suspended') },
    { kind: 'danger', label: memberStatusText('removed') },
    { kind: 'default', label: translate('PortalImportedMembers') },
  ];
}

function sumOf(members: PortalMember[], count: (m: PortalMember) => number) {
  return members.reduce((total, member) => total + count(member), 0);
}

interface MembersFooterProps {
  // The members the filter shows, as Sonarr's footer counts the series shown.
  members: PortalMember[];
}

// The colours the status labels use, how many members the filter shows of each status, and their requests.
function MembersFooter({ members }: MembersFooterProps) {
  const statistics = useMemo(() => {
    const kindCount = (kind: string) =>
      members.filter((m) => memberStatusKind(m.status) === kind).length;

    return [
      [
        { title: translate('PortalMembers'), data: members.length },
        { title: memberStatusText('active'), data: kindCount('success') },
      ],
      [
        { title: memberStatusText('suspended'), data: kindCount('warning') },
        {
          title: translate('PortalImportedMembers'),
          data: kindCount('default'),
        },
      ],
      [
        {
          title: translate('PortalRequests'),
          data: sumOf(members, (m) => m.requests),
        },
        {
          title: translate('PortalPending'),
          data: sumOf(members, (m) => m.pending),
        },
      ],
    ];
  }, [members]);

  return <IndexFooter legend={legend()} statistics={statistics} />;
}

export default MembersFooter;
