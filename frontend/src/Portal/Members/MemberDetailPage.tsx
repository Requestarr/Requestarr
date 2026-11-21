import React, { ReactNode, useCallback, useState } from 'react';
import { useParams } from 'react-router-dom';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import ClipboardButton from 'Components/Link/ClipboardButton';
import Column from 'Components/Table/Column';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Person from '../Components/Person';
import PortalList from '../Components/PortalList';
import PortalPage from '../Components/PortalPage';
import { RemovalRow } from '../Components/RemovalsTable';
import { EventRow, TrimmedNote } from '../Components/TimelineTable';
import useDateText from '../Components/useDateText';
import { accessText, contentTypeText, memberStatusText } from '../portalText';
import {
  PortalEvent,
  PortalMemberDetail,
  PortalRemoval,
  PortalRequest,
} from '../PortalTypes';
import { RequestTableRow } from '../Requests/requestCell';
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
    [
      translate('PortalDiscordId'),
      member.discordId && <DiscordId id={member.discordId} />,
    ],
    [translate('PortalStatus'), memberStatusText(member.status)],
    [translate('PortalImportedFrom'), detail.importedFrom],
    [translate('PortalFirstRequest'), dateText(member.firstRequest)],
    [translate('PortalLastRequest'), dateText(member.lastRequest)],
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

function requestRow(request: PortalRequest, columns: Column[]) {
  return <RequestTableRow request={request} columns={columns} />;
}

function removalRow(removal: PortalRemoval, columns: Column[]) {
  return <RemovalRow removal={removal} columns={columns} />;
}

function eventRow(event: PortalEvent, columns: Column[]) {
  return <EventRow event={event} columns={columns} />;
}

// Their requests, what they follow, their removals and their history, each read a page at a time with its total.
// Their own requests have no Requester column: every row would name them.
function MemberSections({
  detail,
  round,
}: {
  detail: PortalMemberDetail;
  round: number;
}) {
  const query = { id: detail.member.id };

  return (
    <>
      <PortalList
        name="memberRequests"
        query={query}
        round={round}
        title={translate('PortalTheirRequests')}
        emptyText={translate('PortalNoRequests')}
        renderRow={requestRow}
      />
      <PortalList
        name="memberFollowing"
        query={query}
        round={round}
        title={translate('PortalTheyFollow')}
        emptyText={translate('PortalNoRequests')}
        renderRow={requestRow}
      />
      <PortalList
        name="memberRemovals"
        query={query}
        round={round}
        title={translate('PortalRemovals')}
        emptyText={translate('PortalNoRemovals')}
        renderRow={removalRow}
      />
      <PortalList
        name="memberHistory"
        notice={
          detail.historyTrimmedAfter ? (
            <TrimmedNote trimmedAfter={detail.historyTrimmedAfter} />
          ) : undefined
        }
        query={query}
        round={round}
        title={translate('PortalHistory')}
        emptyText={translate('PortalNoHistory')}
        renderRow={eventRow}
      />
    </>
  );
}

interface MemberDetailProps {
  detail: PortalMemberDetail;
  // Counts actions done, so the sections read their pages again after each.
  round: number;
  onDone: () => void;
}

function MemberDetail({ detail, round, onDone }: MemberDetailProps) {
  return (
    <>
      {/* One title, as Sonarr's series page has: their picture, name and @handle, with no heading repeating the name. */}
      <div>
        <div className={styles.memberHeader}>
          <Person
            name={detail.member.name}
            handle={detail.member.discordName}
            avatar={detail.member.avatar}
            size="large"
          />
        </div>
        <Summary detail={detail} />
      </div>

      <MemberActions detail={detail} onDone={onDone} />

      <MemberSections detail={detail} round={round} />
    </>
  );
}

// One member: their access and limits (and changing them), their requests, what they follow, removals they started or
// got, and their history.
function MemberDetailPage() {
  const { id } = useParams<{ id: string }>();
  const result = usePortal<PortalMemberDetail>(`members/${id}?sections=false`);
  const [round, setRound] = useState(0);
  const { reload } = result;
  const onActionDone = useCallback(() => {
    reload();
    setRound((current) => current + 1);
  }, [reload]);
  const renderDetail = useCallback(
    (detail: PortalMemberDetail) => (
      <MemberDetail detail={detail} round={round} onDone={onActionDone} />
    ),
    [round, onActionDone]
  );

  return (
    <PortalPage title={translate('PortalMember')} result={result}>
      {renderDetail}
    </PortalPage>
  );
}

export default MemberDetailPage;
