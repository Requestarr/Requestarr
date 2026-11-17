import React, { ReactNode } from 'react';
import { useParams } from 'react-router-dom';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import PortalPage from '../Components/PortalPage';
import RemovalsTable from '../Components/RemovalsTable';
import RequestsTable from '../Components/RequestsTable';
import TimelineTable from '../Components/TimelineTable';
import useDateText from '../Components/useDateText';
import { accessText, contentTypeText, memberStatusText } from '../portalText';
import { PortalMemberDetail } from '../PortalTypes';
import usePortal from '../usePortal';

// The member's rows, title then value: who they are, then one row per type they have a setting for; a row with
// nothing to show is left out.
function memberRows(
  detail: PortalMemberDetail,
  dateText: (date: string | null) => string
): [string, ReactNode][] {
  const { member } = detail;
  const rows: [string, ReactNode][] = [
    [translate('PortalDiscordName'), member.discordName],
    [translate('PortalStatus'), memberStatusText(member.status)],
    [translate('PortalAdded'), dateText(member.added)],
    [
      translate('PortalMediaServer'),
      detail.mediaServer ?? translate('PortalNotLinked'),
    ],
    ...member.access.map((access): [string, ReactNode] => [
      contentTypeText(access.contentType),
      [accessText(access), access.instance].filter(Boolean).join(' · '),
    ]),
  ];

  return rows.filter(([, value]) => value);
}

function Summary({ detail }: { detail: PortalMemberDetail }) {
  const dateText = useDateText();

  return (
    <DescriptionList>
      {memberRows(detail, dateText).map(([title, value]) => (
        <DescriptionListItem key={title} title={title} data={value} />
      ))}
    </DescriptionList>
  );
}

function renderDetail(detail: PortalMemberDetail) {
  return (
    <>
      <FieldSet legend={detail.member.name}>
        <Summary detail={detail} />
      </FieldSet>

      <FieldSet legend={translate('PortalTheirRequests')}>
        <RequestsTable requests={detail.requests} />
      </FieldSet>

      <FieldSet legend={translate('PortalTheyFollow')}>
        <RequestsTable requests={detail.following} />
      </FieldSet>

      <FieldSet legend={translate('PortalRemovals')}>
        <RemovalsTable removals={detail.removals} />
      </FieldSet>

      <FieldSet legend={translate('PortalHistory')}>
        <TimelineTable events={detail.history} />
      </FieldSet>
    </>
  );
}

// One member: their access and limits, their requests, what they follow, removals they started or got, and their history.
function MemberDetailPage() {
  const { id } = useParams<{ id: string }>();
  const result = usePortal<PortalMemberDetail>(`members/${id}`);

  return (
    <PortalPage title={translate('PortalMember')} result={result}>
      {renderDetail}
    </PortalPage>
  );
}

export default MemberDetailPage;
