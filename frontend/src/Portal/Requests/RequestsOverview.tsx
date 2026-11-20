import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import MemberLink, { MemberLinks } from '../Components/MemberLink';
import useDateText from '../Components/useDateText';
import {
  contentTypeText,
  requestStatusText,
  seasonsText,
  titleText,
} from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { OpenInLink, releaseText, RequestPoster } from './RequestMedia';
import portalStyles from '../Portal.css';
import styles from './RequestViews.css';

// Who asked for it, then who follows it, on a line of their own.
function People({ request }: { request: PortalRequest }) {
  const { followers } = request;

  return (
    <div className={styles.people}>
      <MemberLink person={request.requester} />
      {followers.length > 0 && (
        <MemberLinks
          people={followers}
          before={
            <span className={portalStyles.muted}>
              {translate('PortalFollowedBy')}
            </span>
          }
        />
      )}
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
      <RequestPoster url={media?.poster} className={styles.overviewPoster} />
      <div className={styles.overviewBody}>
        <div className={styles.overviewTitle}>
          <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
        </div>
        <div>
          {contentTypeText(request.contentType)} · {requestStatusText(request)}{' '}
          · {dateText(request.requested)}
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
