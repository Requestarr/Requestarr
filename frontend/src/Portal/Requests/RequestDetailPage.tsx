import React, { ReactNode, useCallback, useEffect, useState } from 'react';
import { useLocation, useParams } from 'react-router-dom';
import Alert from 'Components/Alert';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import FieldSet from 'Components/FieldSet';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import Column from 'Components/Table/Column';
import Tooltip from 'Components/Tooltip/Tooltip';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import DetailsHeader, {
  detailsStyles,
} from '../Components/Details/DetailsHeader';
import HeaderLabel from '../Components/Details/HeaderLabel';
import PortalList from '../Components/PortalList';
import RemovalsTable from '../Components/RemovalsTable';
import { EventRow, TrimmedNote } from '../Components/TimelineTable';
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
import FollowerRow from './FollowerRow';
import { OpenInLink, releaseText } from './RequestMedia';
import RequestPoster from './RequestPoster';
import { requestStatusKind } from './RequestStatusLabel';
import useRequestActions from './useRequestActions';
import styles from '../Portal.css';

// The Followers section's address, which a list view's follower stack opens.
const FOLLOWERS_ID = 'followers';

// The title on The Movie Database or TheTVDB, and its page in Sonarr or Radarr when a browser can reach it, as Sonarr
// main's SeriesDetailsLinks lists a series' sites.
function TitleLinks({ request }: { request: PortalRequest }) {
  const page = request.media?.page;

  return (
    <div className={styles.links}>
      {page && (
        <Link to={page}>
          {translate(
            page.includes('thetvdb') ? 'PortalOnTvdb' : 'PortalOnTmdb'
          )}
        </Link>
      )}
      <OpenInLink media={request.media} />
    </div>
  );
}

// The header's chips, as Sonarr's series page has its status, network and links: status, type, who asked, who follows,
// the media manager and when it was asked for, then the title's links on hover.
function RequestLabels({ request }: { request: PortalRequest }) {
  const dateText = useDateText();
  const followers = request.followers.length;

  return (
    <>
      <HeaderLabel
        iconName={icons.FLAG}
        title={translate('PortalStatus')}
        kind={requestStatusKind(request)}
      >
        {requestStatusText(request)}
      </HeaderLabel>
      <HeaderLabel
        iconName={request.contentType === 'tv' ? icons.TV : icons.FILM}
        title={translate('PortalType')}
      >
        {[contentTypeText(request.contentType), seasonsText(request.seasons)]
          .filter(Boolean)
          .join(' · ')}
      </HeaderLabel>
      <Link to={`/members/${request.requester.id}`}>
        <HeaderLabel
          iconName={icons.GROUP}
          title={translate('PortalRequester')}
        >
          {request.requester.name ?? translate('PortalUnknownMember')}
        </HeaderLabel>
      </Link>
      {followers > 0 && (
        <Link to={`#${FOLLOWERS_ID}`}>
          <HeaderLabel
            iconName={icons.HEART}
            title={translate('PortalFollowers')}
          >
            {translate('PortalCountFollowing', { count: followers })}
          </HeaderLabel>
        </Link>
      )}
      <HeaderLabel iconName={icons.DRIVE} title={translate('PortalInstance')}>
        {request.instance ?? translate('PortalNotSent')}
      </HeaderLabel>
      <HeaderLabel iconName={icons.ADD} title={translate('PortalRequested')}>
        {dateText(request.requested)}
      </HeaderLabel>
      {(request.media?.page || request.media?.openUrl) && (
        <Tooltip
          anchor={
            <HeaderLabel iconName={icons.EXTERNAL_LINK}>
              {translate('PortalLinks')}
            </HeaderLabel>
          }
          tooltip={<TitleLinks request={request} />}
          kind={kinds.INVERSE}
          position={tooltipPositions.BOTTOM}
        />
      )}
    </>
  );
}

// What the header doesn't say: progress, the decision's reason, the seasons there, and when it was decided and
// fulfilled; a row with nothing to show is left out.
function moreRows(
  detail: PortalRequestDetail,
  dateText: (date: string | null) => string
): [string, ReactNode][] {
  const { request } = detail;
  const rows: [string, ReactNode][] = [
    ['PortalProgress', detail.progressDetail],
    ['PortalReason', detail.decisionReason],
    ['PortalSeasonsThere', seasonsText(detail.availableSeasons)],
    ['PortalDecided', dateText(request.decided)],
    ['PortalFulfilled', dateText(request.fulfilled)],
  ];

  return rows.filter(([, value]) => value);
}

function MoreDetails({ detail }: { detail: PortalRequestDetail }) {
  const rows = moreRows(detail, useDateText());

  if (!rows.length) {
    return null;
  }

  return (
    <FieldSet legend={translate('Details')}>
      <DescriptionList>
        {rows.map(([title, value]) => (
          <DescriptionListItem
            key={title}
            title={translate(title)}
            data={value}
          />
        ))}
      </DescriptionList>
    </FieldSet>
  );
}

function followerRow(follower: PortalFollower, columns: Column[]) {
  return <FollowerRow follower={follower} columns={columns} />;
}

function eventRow(event: PortalEvent, columns: Column[]) {
  return <EventRow event={event} columns={columns} />;
}

// Opened at #followers, the page scrolls to that section once it's there.
function useScrollToFollowers() {
  const { hash } = useLocation();

  useEffect(() => {
    if (hash === `#${FOLLOWERS_ID}`) {
      document.getElementById(FOLLOWERS_ID)?.scrollIntoView();
    }
  }, [hash]);
}

interface RequestDetailProps {
  detail: PortalRequestDetail;
  // Counts actions done, so the sections read their pages again after each.
  round: number;
}

// Below the header: the rest of its details, its followers, its timeline and its latest removal.
function RequestSections({ detail, round }: RequestDetailProps) {
  const { request, latestRemoval, historyTrimmedAfter } = detail;
  const query = { id: request.id };
  useScrollToFollowers();

  return (
    <div className={detailsStyles.contentContainer}>
      <MoreDetails detail={detail} />

      {request.followers.length > 0 && (
        <div id={FOLLOWERS_ID}>
          <PortalList
            name="requestFollowers"
            query={query}
            title={translate('PortalFollowers')}
            emptyText={translate('PortalNobody')}
            round={round}
            renderRow={followerRow}
          />
        </div>
      )}

      <PortalList
        name="requestTimeline"
        notice={
          historyTrimmedAfter ? (
            <TrimmedNote trimmedAfter={historyTrimmedAfter} />
          ) : undefined
        }
        query={query}
        title={translate('PortalTimeline')}
        emptyText={translate('PortalNoHistory')}
        round={round}
        renderRow={eventRow}
      />

      {latestRemoval && (
        <FieldSet legend={translate('PortalLatestRemoval')}>
          <RemovalsTable removal={latestRemoval} />
        </FieldSet>
      )}
    </div>
  );
}

// The page once the request has arrived, as Sonarr main's SeriesDetails: its actions in the toolbar, then the header
// and the sections.
function RequestDetail({
  detail,
  round,
  onDone,
}: RequestDetailProps & { onDone: () => void }) {
  const { request } = detail;
  const actions = useRequestActions(detail, onDone);

  return (
    <>
      <PageToolbar>
        <PageToolbarSection>{actions.buttons}</PageToolbarSection>
      </PageToolbar>

      <PageContentBody innerClassName={detailsStyles.innerContentBody}>
        <DetailsHeader
          backdropUrl={detail.posterUrl}
          image={
            <RequestPoster
              className={detailsStyles.posterImage}
              url={detail.posterUrl}
              lazy={false}
            />
          }
          title={titleText(request)}
          details={releaseText(request.media)}
          labels={<RequestLabels request={request} />}
          overview={request.media?.overview}
        />
        <RequestSections detail={detail} round={round} />
      </PageContentBody>

      {actions.dialogs}
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

  return (
    <PageContent title={translate('PortalRequest')}>
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
        <RequestDetail
          detail={result.data}
          round={round}
          onDone={onActionDone}
        />
      )}
    </PageContent>
  );
}

export default RequestDetailPage;
