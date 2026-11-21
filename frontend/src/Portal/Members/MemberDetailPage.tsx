import React, { ReactNode, useCallback, useState } from 'react';
import { useParams } from 'react-router-dom';
import Alert from 'Components/Alert';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import ClipboardButton from 'Components/Link/ClipboardButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import Column from 'Components/Table/Column';
import { icons, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Avatar from '../Components/Avatar';
import DetailsHeader, {
  detailsStyles,
} from '../Components/Details/DetailsHeader';
import HeaderLabel from '../Components/Details/HeaderLabel';
import PortalList from '../Components/PortalList';
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
import { memberStatusKind } from './MemberStatusLabel';
import useMemberDetailActions from './useMemberDetailActions';
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
    <FieldSet legend={translate('Details')}>
      <DescriptionList>
        {memberRows(detail, dateText).map(([title, value]) => (
          <DescriptionListItem key={title} title={title} data={value} />
        ))}
      </DescriptionList>
    </FieldSet>
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

// The page once the member has arrived, as Sonarr main's SeriesDetails: their actions in the toolbar, then the header
// with their picture, name, @handle and status, then their details and sections.
function MemberDetail({ detail, round, onDone }: MemberDetailProps) {
  const { member } = detail;
  const actions = useMemberDetailActions(detail, onDone);

  return (
    <>
      <PageToolbar>
        <PageToolbarSection>{actions.buttons}</PageToolbarSection>
      </PageToolbar>

      <PageContentBody innerClassName={detailsStyles.innerContentBody}>
        <DetailsHeader
          image={<Avatar url={member.avatar} size="large" />}
          title={member.name}
          details={member.discordName ? `@${member.discordName}` : null}
          labels={
            <HeaderLabel
              iconName={icons.FLAG}
              title={translate('PortalStatus')}
              kind={memberStatusKind(member.status)}
            >
              {memberStatusText(member.status)}
            </HeaderLabel>
          }
        />
        <div className={detailsStyles.contentContainer}>
          <Summary detail={detail} />
          <MemberSections detail={detail} round={round} />
        </div>
      </PageContentBody>

      {actions.dialogs}
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

  return (
    <PageContent title={translate('PortalMember')}>
      {result.data === undefined ? (
        <PageContentBody>
          {result.isNotFound ? (
            <NotFound message={translate('PortalNotFound')} />
          ) : (
            <>
              {result.isLoading && <LoadingIndicator />}
              {result.hasError && (
                <Alert kind={kinds.DANGER}>
                  {translate('PortalLoadError')}
                </Alert>
              )}
            </>
          )}
        </PageContentBody>
      ) : (
        <MemberDetail
          detail={result.data}
          round={round}
          onDone={onActionDone}
        />
      )}
    </PageContent>
  );
}

export default MemberDetailPage;
