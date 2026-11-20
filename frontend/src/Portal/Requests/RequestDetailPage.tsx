import React, { ReactNode, useCallback, useEffect, useState } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import FollowerStack from '../Components/FollowerStack';
import MemberLink from '../Components/MemberLink';
import PagedSection, { PagedSort } from '../Components/Paging/PagedSection';
import PortalPage from '../Components/PortalPage';
import RemovalsTable from '../Components/RemovalsTable';
import TimelineTable from '../Components/TimelineTable';
import useDateText from '../Components/useDateText';
import {
  contentTypeText,
  requestStatusText,
  seasonsText,
  titleText,
} from '../portalText';
import {
  PortalEvent,
  PortalFollower,
  PortalRequest,
  PortalRequestDetail,
} from '../PortalTypes';
import usePortal from '../usePortal';
import FollowersTable from './FollowersTable';
import RequestActions from './RequestActions';
import { OpenInLink, releaseText, RequestPoster } from './RequestMedia';
import styles from '../Portal.css';

// The summary's followers as the list views show them; the Followers section below lists everyone.
function Followers({ request }: { request: PortalRequest }) {
  if (!request.followers.length) {
    return <span className={styles.muted}>{translate('PortalNobody')}</span>;
  }

  return <FollowerStack requestId={request.id} followers={request.followers} />;
}

// The title on The Movie Database or TheTVDB, and its page in Sonarr or Radarr when a browser can reach it.
function TitleLinks({ request }: { request: PortalRequest }) {
  const page = request.media?.page;

  if (!page && !request.media?.openUrl) {
    return null;
  }

  return (
    <span className={styles.links}>
      {page && (
        <Link to={page}>
          {translate(
            page.includes('thetvdb') ? 'PortalOnTvdb' : 'PortalOnTmdb'
          )}
        </Link>
      )}
      <OpenInLink media={request.media} />
    </span>
  );
}

// The summary's rows, title then value; a row with nothing to show is left out.
function summaryRows(
  detail: PortalRequestDetail,
  dateText: (date: string | null) => string
): [string, ReactNode][] {
  const { request } = detail;
  const rows: [string, ReactNode][] = [
    ['PortalOverview', request.media?.overview],
    ['PortalReleased', releaseText(request.media)],
    [
      'PortalLinks',
      (request.media?.page || request.media?.openUrl) && (
        <TitleLinks key="l" request={request} />
      ),
    ],
    ['PortalType', contentTypeText(request.contentType)],
    ['PortalStatus', requestStatusText(request)],
    ['PortalProgress', detail.progressDetail],
    ['PortalReason', detail.decisionReason],
    ['PortalRequester', <MemberLink key="r" person={request.requester} />],
    ['PortalFollowers', <Followers key="f" request={request} />],
    ['PortalInstance', request.instance ?? translate('PortalNotSent')],
    ['PortalSeasonsAsked', seasonsText(request.seasons)],
    ['PortalSeasonsThere', seasonsText(detail.availableSeasons)],
    ['PortalRequested', dateText(request.requested)],
    ['PortalDecided', dateText(request.decided)],
    ['PortalFulfilled', dateText(request.fulfilled)],
  ];

  return rows.filter(([, value]) => value);
}

function Summary({ detail }: { detail: PortalRequestDetail }) {
  const dateText = useDateText();

  return (
    <DescriptionList>
      {summaryRows(detail, dateText).map(([title, value]) => (
        <DescriptionListItem
          key={title}
          title={translate(title)}
          data={value}
        />
      ))}
    </DescriptionList>
  );
}

interface RequestDetailProps {
  detail: PortalRequestDetail;
  // Counts actions done, so the sections read their pages again after each.
  round: number;
  onDone: () => void;
}

function followersPage(records: PortalFollower[], sort: PagedSort) {
  return <FollowersTable followers={records} sort={sort} />;
}

// The Followers section's address, which a list view's follower stack opens.
const FOLLOWERS_ID = 'followers';

// Opened at #followers, the page scrolls to that section once it's there.
function useScrollToFollowers() {
  const { hash } = useLocation();

  useEffect(() => {
    if (hash === `#${FOLLOWERS_ID}`) {
      document.getElementById(FOLLOWERS_ID)?.scrollIntoView();
    }
  }, [hash]);
}

function RequestDetail({ detail, round, onDone }: RequestDetailProps) {
  const { request, latestRemoval, historyTrimmedAfter } = detail;
  const path = `requests/${request.id}`;
  useScrollToFollowers();
  const timelinePage = useCallback(
    (records: PortalEvent[]) => (
      <TimelineTable events={records} trimmedAfter={historyTrimmedAfter} />
    ),
    [historyTrimmedAfter]
  );

  return (
    <>
      <FieldSet legend={titleText(request)}>
        <div className={styles.detail}>
          <RequestPoster url={detail.posterUrl} className={styles.poster} />
          <Summary detail={detail} />
        </div>
      </FieldSet>

      <RequestActions detail={detail} onDone={onDone} />

      {request.followers.length > 0 && (
        <div id={FOLLOWERS_ID}>
          <PagedSection
            key={round}
            title={translate('PortalFollowers')}
            path={`${path}/followers`}
            initialSort="followed"
          >
            {followersPage}
          </PagedSection>
        </div>
      )}

      <PagedSection
        key={round}
        title={translate('PortalTimeline')}
        path={`${path}/timeline`}
      >
        {timelinePage}
      </PagedSection>

      {latestRemoval && (
        <FieldSet legend={translate('PortalLatestRemoval')}>
          <RemovalsTable removals={[latestRemoval]} />
        </FieldSet>
      )}
    </>
  );
}

// One request: what it is, where it stands, who asked and followed, everything that happened to it, and what an admin
// can do to it now.
function RequestDetailPage() {
  const { id } = useParams<{ id: string }>();
  const result = usePortal<PortalRequestDetail>(
    `requests/${id}?sections=false`
  );
  const [round, setRound] = useState(0);
  const { reload } = result;
  const onActionDone = useCallback(() => {
    reload();
    setRound((current) => current + 1);
  }, [reload]);
  const renderDetail = useCallback(
    (detail: PortalRequestDetail) => (
      <RequestDetail detail={detail} round={round} onDone={onActionDone} />
    ),
    [round, onActionDone]
  );

  return (
    <PortalPage title={translate('PortalRequest')} result={result}>
      {renderDetail}
    </PortalPage>
  );
}

export default RequestDetailPage;
