import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import FollowerStack from '../Components/FollowerStack';
import MemberLink from '../Components/MemberLink';
import useDateText from '../Components/useDateText';
import {
  contentTypeText,
  requestStatusText,
  seasonsText,
  titleText,
} from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { OpenInLink, releaseText, RequestPoster } from './RequestMedia';
import { RowActions, RowSelect } from './RequestRows';
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

function OverviewRow({ request }: { request: PortalRequest }) {
  const dateText = useDateText();
  const { media } = request;
  const details = [seasonsText(request.seasons), releaseText(media)]
    .filter(Boolean)
    .join(' · ');

  return (
    <div className={styles.overviewRow}>
      <RowSelect request={request} />
      <RequestPoster url={media?.poster} className={styles.overviewPoster} />
      <div className={styles.overviewBody}>
        <div className={styles.overviewTitle}>
          <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
        </div>
        <div className={styles.overviewMeta}>
          <span>
            {contentTypeText(request.contentType)} ·{' '}
            {requestStatusText(request)} · {dateText(request.requested)}
          </span>
          <RowActions request={request} />
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

// Each request with its poster, who asked and when, and what the title is: its overview and when it came out.
function RequestsOverview({ requests }: { requests: PortalRequest[] }) {
  if (!requests.length) {
    return (
      <div className={portalStyles.empty}>{translate('PortalNoRequests')}</div>
    );
  }

  return (
    <div>
      {requests.map((request) => (
        <OverviewRow key={request.id} request={request} />
      ))}
    </div>
  );
}

export default RequestsOverview;
