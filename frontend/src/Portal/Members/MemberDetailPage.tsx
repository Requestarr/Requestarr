import React, { ReactNode, useCallback } from 'react';
import { useParams } from 'react-router-dom';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import ClipboardButton from 'Components/Link/ClipboardButton';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import PortalPage from '../Components/PortalPage';
import RemovalsTable from '../Components/RemovalsTable';
import RequestsTable from '../Components/RequestsTable';
import TimelineTable from '../Components/TimelineTable';
import useDateText from '../Components/useDateText';
import { accessText, contentTypeText, memberStatusText } from '../portalText';
import { PortalMemberDetail } from '../PortalTypes';
import usePortal from '../usePortal';
import MemberActions from './MemberActions';
import styles from '../Portal.css';

// Their Discord ID, which is how Requestarr knows them whatever they're called, with a button to copy it.
function DiscordId({ id }: { id: string }) {
  return (
    <span className={styles.discordId}>
      {id}
      <ClipboardButton value={id} kind={kinds.DEFAULT} />
    </span>
  );
}

// The member's rows, title then value: who they are, then one row per type they have a setting for; a row with
// nothing to show is left out.
function memberRows(
  detail: PortalMemberDetail,
  dateText: (date: string | null) => string
): [string, ReactNode][] {
  const { member } = detail;
  const rows: [string, ReactNode][] = [
    [translate('PortalDiscordName'), member.discordName],
    [
      translate('PortalDiscordId'),
      member.discordId && <DiscordId id={member.discordId} />,
    ],
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

interface MemberDetailProps {
  detail: PortalMemberDetail;
  onDone: () => void;
}

function MemberDetail({ detail, onDone }: MemberDetailProps) {
  return (
    <>
      <FieldSet legend={detail.member.name}>
        <Summary detail={detail} />
      </FieldSet>

      <MemberActions detail={detail} onDone={onDone} />

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

// One member: their access and limits (and changing them), their requests, what they follow, removals they started or
// got, and their history.
function MemberDetailPage() {
  const { id } = useParams<{ id: string }>();
  const result = usePortal<PortalMemberDetail>(`members/${id}`);
  const onActionDone = result.reload;
  const renderDetail = useCallback(
    (detail: PortalMemberDetail) => (
      <MemberDetail detail={detail} onDone={onActionDone} />
    ),
    [onActionDone]
  );

  return (
    <PortalPage title={translate('PortalMember')} result={result}>
      {renderDetail}
    </PortalPage>
  );
}

export default MemberDetailPage;
