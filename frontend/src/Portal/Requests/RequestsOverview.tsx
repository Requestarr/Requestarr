import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import FollowerStack from '../Components/FollowerStack';
import MemberLink from '../Components/MemberLink';
import PosterSelect from '../Components/Select/PosterSelect';
import useDateText from '../Components/useDateText';
import { contentTypeText, seasonsText, titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { OpenInLink, releaseText, RequestPoster } from './RequestMedia';
import RequestStatusLabel from './RequestStatusLabel';
import portalStyles from '../Portal.css';
import styles from './RequestViews.css';

// Who asked for it, then who follows it, on one line however many follow it.
function People({ request }: { request: PortalRequest }) {
  return (
    <div className={styles.people}>
      <MemberLink person={request.requester} size="small" />
      <FollowerStack requestId={request.id} followers={request.followers} />
    </div>
  );
}

interface OverviewRowProps {
  request: PortalRequest;
  isSelectMode: boolean;
}

function OverviewRow({ request, isSelectMode }: OverviewRowProps) {
  const dateText = useDateText();
  const { media } = request;
  const details = [seasonsText(request.seasons), releaseText(media)]
    .filter(Boolean)
    .join(' · ');

  return (
    <div className={styles.overviewRow}>
      <div className={styles.posterContainer}>
        {isSelectMode && <PosterSelect itemId={request.id} />}
        <RequestPoster url={media?.poster} className={styles.overviewPoster} />
      </div>
      <div className={styles.overviewBody}>
        <div className={styles.overviewTitle}>
          <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
        </div>
        <div className={styles.overviewMeta}>
          <span>
            {contentTypeText(request.contentType)} ·{' '}
            {dateText(request.requested)}
          </span>
          <RequestStatusLabel request={request} />
        </div>
        <People request={request} />
        {media?.overview && (
          <div className={styles.overviewText}>{media.overview}</div>
        )}
        {details && <div className={portalStyles.muted}>{details}</div>}
        <OpenInLink media={media} />
      </div>
    </div>
  );
}

interface RequestsOverviewProps {
  requests: PortalRequest[];
  isSelectMode: boolean;
}

// Each request with its poster, who asked and when, and what the title is: its overview and when it came out; in
// select mode a round tick on each poster, as Sonarr's overview has.
function RequestsOverview({ requests, isSelectMode }: RequestsOverviewProps) {
  if (!requests.length) {
    return (
      <div className={portalStyles.empty}>{translate('PortalNoRequests')}</div>
    );
  }

  return (
    <div>
      {requests.map((request) => (
        <OverviewRow
          key={request.id}
          request={request}
          isSelectMode={isSelectMode}
        />
      ))}
    </div>
  );
}

export default RequestsOverview;
