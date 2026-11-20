import React, { ReactNode, useCallback, useState } from 'react';
import { useParams } from 'react-router-dom';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import ClipboardButton from 'Components/Link/ClipboardButton';
import { kinds, sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import PagedSection, { PagedSort } from '../Components/Paging/PagedSection';
import Person from '../Components/Person';
import PortalPage from '../Components/PortalPage';
import RemovalsTable from '../Components/RemovalsTable';
import RequestsTable from '../Components/RequestsTable';
import TimelineTable from '../Components/TimelineTable';
import useDateText from '../Components/useDateText';
import { accessText, contentTypeText, memberStatusText } from '../portalText';
import {
  PortalEvent,
  PortalMemberDetail,
  PortalRemoval,
  PortalRequest,
} from '../PortalTypes';
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

// A page of requests in a member's section, sorted by the server.
function requestsPage(withRequester: boolean) {
  return function RequestsPage(records: PortalRequest[], sort: PagedSort) {
    return (
      <RequestsTable
        requests={records}
        sort={{ ...sort, sorted: records }}
        withRequester={withRequester}
      />
    );
  };
}

// Their own requests have no Requester column: every row would name them.
const ownRequests = requestsPage(false);
const followedRequests = requestsPage(true);

function removalsPage(records: PortalRemoval[]) {
  return <RemovalsTable removals={records} />;
}

// Their requests, what they follow, their removals and their history, each paged on its own with its total.
function MemberSections({ detail }: { detail: PortalMemberDetail }) {
  const path = `members/${detail.member.id}`;
  const { historyTrimmedAfter } = detail;
  const historyPage = useCallback(
    (records: PortalEvent[]) => (
      <TimelineTable events={records} trimmedAfter={historyTrimmedAfter} />
    ),
    [historyTrimmedAfter]
  );

  return (
    <>
      <PagedSection
        title={translate('PortalTheirRequests')}
        path={`${path}/requests`}
        initialSort="requested"
        initialDirection={sortDirections.DESCENDING}
      >
        {ownRequests}
      </PagedSection>
      <PagedSection
        title={translate('PortalTheyFollow')}
        path={`${path}/following`}
        initialSort="requested"
        initialDirection={sortDirections.DESCENDING}
      >
        {followedRequests}
      </PagedSection>
      <PagedSection
        title={translate('PortalRemovals')}
        path={`${path}/removals`}
      >
        {removalsPage}
      </PagedSection>
      <PagedSection title={translate('PortalHistory')} path={`${path}/history`}>
        {historyPage}
      </PagedSection>
    </>
  );
}

interface MemberDetailProps {
  detail: PortalMemberDetail;
  onDone: () => void;
}

// round: counts actions done, so the sections read their pages again after each.
function MemberDetail({
  detail,
  round,
  onDone,
}: MemberDetailProps & { round: number }) {
  return (
    <>
      <FieldSet legend={detail.member.name}>
        <div className={styles.memberHeader}>
          <Person
            name={detail.member.name}
            handle={detail.member.discordName}
            avatar={detail.member.avatar}
            size="large"
          />
        </div>
        <Summary detail={detail} />
      </FieldSet>

      <MemberActions detail={detail} onDone={onDone} />

      <MemberSections key={round} detail={detail} />
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
