import React from 'react';
import ProgressBar from 'Components/ProgressBar';
import { sizes } from 'Helpers/Props';
import { requestStatusText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { requestStatusKind } from './RequestStatusLabel';
import styles from './RequestProgressBar.css';

// A request is in one status, so the bar is always full; its colour is the status's, as the footer's legend shows.
const FULL = 100;

interface RequestProgressBarProps {
  request: PortalRequest;
  width: number;
  detailedProgressBar: boolean;
}

// The bar under a request's poster, as Sonarr main's Series/Index/ProgressBar/SeriesIndexProgressBar draws one under a
// series: coloured by the request's status, with the status written in it when the options ask for the detailed bar.
function RequestProgressBar(props: RequestProgressBarProps) {
  const { request, width, detailedProgressBar } = props;
  const text = requestStatusText(request);

  return (
    <ProgressBar
      className={styles.progressBar}
      containerClassName={styles.progress}
      progress={FULL}
      kind={requestStatusKind(request)}
      size={detailedProgressBar ? sizes.MEDIUM : sizes.SMALL}
      showText={detailedProgressBar}
      text={text}
      title={text}
      width={width}
    />
  );
}

export default RequestProgressBar;
