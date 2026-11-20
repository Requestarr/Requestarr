import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { FollowerBadge } from '../Components/FollowerStack';
import MemberLink from '../Components/MemberLink';
import PosterSelect from '../Components/Select/PosterSelect';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { OpenInLink } from './RequestMedia';
import RequestStatusLabel from './RequestStatusLabel';
import portalStyles from '../Portal.css';
import styles from './RequestViews.css';

// The poster, or the title on a plain tile when there's none (or it doesn't load).
function Poster({ request }: { request: PortalRequest }) {
  const [failed, setFailed] = useState(false);
  const onError = useCallback(() => setFailed(true), []);
  const poster = request.media?.poster;

  return (
    <Link to={`/requests/${request.id}`}>
      {poster && !failed ? (
        <img
          className={styles.posterImage}
          src={poster}
          alt={titleText(request)}
          loading="lazy"
          onError={onError}
        />
      ) : (
        <div className={styles.posterMissing}>{titleText(request)}</div>
      )}
    </Link>
  );
}

interface RequestsPostersProps {
  requests: PortalRequest[];
  isSelectMode: boolean;
}

// Every request as a poster, with its title, status, requester and follower count under it; in select mode a round
// tick on each poster, as Sonarr's posters have.
function RequestsPosters({ requests, isSelectMode }: RequestsPostersProps) {
  if (!requests.length) {
    return (
      <div className={portalStyles.empty}>{translate('PortalNoRequests')}</div>
    );
  }

  return (
    <div className={styles.posters}>
      {requests.map((request) => (
        <div key={request.id} className={styles.posterCard}>
          <div className={styles.posterContainer}>
            {isSelectMode && <PosterSelect itemId={request.id} />}
            <Poster request={request} />
          </div>
          <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
          <div className={styles.posterStatus}>
            <RequestStatusLabel request={request} />
          </div>
          <div className={styles.posterPeople}>
            <MemberLink person={request.requester} size="small" />
            <FollowerBadge
              requestId={request.id}
              followers={request.followers}
            />
          </div>
          <OpenInLink media={request.media} />
        </div>
      ))}
    </div>
  );
}

export default RequestsPosters;
