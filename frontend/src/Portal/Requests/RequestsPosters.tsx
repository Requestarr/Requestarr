import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import MemberLink from '../Components/MemberLink';
import { requestStatusText, titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { OpenInLink } from './RequestMedia';
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

// Every request as a poster, with its title, status and requester under it.
function RequestsPosters({ requests }: { requests: PortalRequest[] }) {
  if (!requests.length) {
    return (
      <div className={portalStyles.empty}>{translate('PortalNoRequests')}</div>
    );
  }

  return (
    <div className={styles.posters}>
      {requests.map((request) => (
        <div key={request.id} className={styles.posterCard}>
          <Poster request={request} />
          <Link to={`/requests/${request.id}`}>{titleText(request)}</Link>
          <div className={portalStyles.muted}>{requestStatusText(request)}</div>
          <MemberLink person={request.requester} />
          <OpenInLink media={request.media} />
        </div>
      ))}
    </div>
  );
}

export default RequestsPosters;
