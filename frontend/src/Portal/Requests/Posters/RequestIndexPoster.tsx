import React, { useCallback, useState } from 'react';
import { useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import PosterSelect from '../../Components/Select/PosterSelect';
import { titleText } from '../../portalText';
import { PortalRequest } from '../../PortalTypes';
import RequestPoster from '../RequestPoster';
import RequestProgressBar from '../RequestProgressBar';
import RequestIndexPosterInfo from './RequestIndexPosterInfo';
import styles from './RequestIndexPoster.css';

interface RequestIndexPosterProps {
  request: PortalRequest;
  sortKey: string;
  isSelectMode: boolean;
  posterWidth: number;
  posterHeight: number;
}

// Who follows it, as "3 following"; nothing when nobody does.
function followersText(request: PortalRequest) {
  return request.followers.length
    ? translate('PortalCountFollowing', { count: request.followers.length })
    : '';
}

// One request in the Posters view, as Sonarr main's Series/Index/Posters/SeriesIndexPoster draws a series: its poster
// (the title over the placeholder when there's none), the round tick in select mode, the status bar, then the lines
// the poster options turn on and the one the sort adds.
function RequestIndexPoster(props: RequestIndexPosterProps) {
  const { request, sortKey, isSelectMode, posterWidth, posterHeight } = props;
  const { detailedProgressBar, showTitle, showRequester, showFollowers } =
    useSelector((state: AppState) => state.requestIndex.posterOptions);
  const [hasPosterError, setHasPosterError] = useState(false);
  const onPosterLoadError = useCallback(() => setHasPosterError(true), []);
  const onPosterLoad = useCallback(() => setHasPosterError(false), []);

  const title = titleText(request);
  const link = `/requests/${request.id}`;
  const elementStyle = {
    width: `${posterWidth}px`,
    height: `${posterHeight}px`,
  };

  return (
    <div className={styles.content}>
      <div className={styles.posterContainer} title={title}>
        {isSelectMode ? <PosterSelect itemId={request.id} /> : null}

        <Link className={styles.link} style={elementStyle} to={link}>
          <RequestPoster
            style={elementStyle}
            url={request.media?.poster}
            lazy={false}
            onError={onPosterLoadError}
            onLoad={onPosterLoad}
          />

          {hasPosterError ? (
            <div className={styles.overlayTitle}>{title}</div>
          ) : null}
        </Link>
      </div>

      <RequestProgressBar
        request={request}
        width={posterWidth}
        detailedProgressBar={detailedProgressBar}
      />

      {showTitle ? (
        <div className={styles.title} title={title}>
          {title}
        </div>
      ) : null}

      {showRequester ? (
        <div className={styles.title} title={translate('PortalRequester')}>
          <Link to={`/members/${request.requester.id}`}>
            {request.requester.name ?? translate('PortalUnknownMember')}
          </Link>
        </div>
      ) : null}

      {showFollowers ? (
        <div className={styles.title} title={translate('PortalFollowers')}>
          {followersText(request)}
        </div>
      ) : null}

      <RequestIndexPosterInfo request={request} sortKey={sortKey} />
    </div>
  );
}

export default RequestIndexPoster;
