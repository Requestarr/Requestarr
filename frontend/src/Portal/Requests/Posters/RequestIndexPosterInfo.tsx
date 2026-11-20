import React from 'react';
import { useSelector } from 'react-redux';
import createUISettingsSelector from 'Store/Selectors/createUISettingsSelector';
import formatDateTime from 'Utilities/Date/formatDateTime';
import translate from 'Utilities/String/translate';
import relativeDate from '../../Components/relativeDate';
import { contentTypeText, requestStatusText } from '../../portalText';
import { PortalRequest } from '../../PortalTypes';
import styles from './RequestIndexPosterInfo.css';

const DATE_SORTS = {
  requested: 'PortalRequested',
  decided: 'PortalDecided',
  fulfilled: 'PortalFulfilled',
} as const;

type DateSort = keyof typeof DATE_SORTS;

interface RequestIndexPosterInfoProps {
  request: PortalRequest;
  sortKey: string;
}

// The line that says what the posters are sorted by, as Sonarr main's SeriesIndexPosterInfo shows the network, the
// date added and so on under each poster. Nothing for a sort the poster already shows (title, requester, followers).
function RequestIndexPosterInfo({
  request,
  sortKey,
}: RequestIndexPosterInfoProps) {
  const { showRelativeDates, shortDateFormat, longDateFormat, timeFormat } =
    useSelector(createUISettingsSelector());

  if (sortKey === 'type') {
    return (
      <div className={styles.info} title={translate('PortalType')}>
        {contentTypeText(request.contentType)}
      </div>
    );
  }

  if (sortKey === 'status') {
    return (
      <div className={styles.info} title={translate('PortalStatus')}>
        {requestStatusText(request)}
      </div>
    );
  }

  if (sortKey === 'instance' && request.instance) {
    return (
      <div className={styles.info} title={translate('PortalInstance')}>
        {request.instance}
      </div>
    );
  }

  const date = request[sortKey as DateSort];

  if (sortKey in DATE_SORTS && date) {
    return (
      <div
        className={styles.info}
        title={`${translate(DATE_SORTS[sortKey as DateSort])}: ${formatDateTime(
          date,
          longDateFormat,
          timeFormat
        )}`}
      >
        {relativeDate(date, shortDateFormat, showRelativeDates, {
          timeFormat,
          timeForToday: true,
        })}
      </div>
    );
  }

  return null;
}

export default RequestIndexPosterInfo;
