import React from 'react';
import TextTruncate from 'react-text-truncate';
import { RequestOverviewOptions } from 'App/State/PortalAppState';
import Link from 'Components/Link/Link';
import dimensions from 'Styles/Variables/dimensions';
import fonts from 'Styles/Variables/fonts';
import PosterSelect from '../../Components/Select/PosterSelect';
import { titleText } from '../../portalText';
import { PortalRequest } from '../../PortalTypes';
import { OpenInLink } from '../RequestMedia';
import RequestPoster from '../RequestPoster';
import RequestProgressBar from '../RequestProgressBar';
import RequestIndexOverviewInfo from './RequestIndexOverviewInfo';
import styles from './RequestIndexOverview.css';

const columnPadding = parseInt(dimensions.requestIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(
  dimensions.requestIndexColumnPaddingSmallScreen
);
const defaultFontSize = parseInt(fonts.defaultFontSize);
const lineHeight = parseFloat(fonts.lineHeight);

// The title's line height of 32 and its bottom margin of 10, as Sonarr's overview has it.
const TITLE_HEIGHT = 42;

interface RequestIndexOverviewProps {
  request: PortalRequest;
  options: RequestOverviewOptions;
  posterWidth: number;
  posterHeight: number;
  rowHeight: number;
  isSelectMode: boolean;
  isSmallScreen: boolean;
}

// One request in the Overview view, as Sonarr main's Series/Index/Overview/SeriesIndexOverview draws a series: the
// poster with its status bar, the title with "Open in Sonarr" where Sonarr has its row's actions, as much of the
// overview as fits, and the rows the options turn on.
function RequestIndexOverview(props: RequestIndexOverviewProps) {
  const { request, options, posterWidth, posterHeight, isSelectMode } = props;
  const link = `/requests/${request.id}`;
  const elementStyle = {
    width: `${posterWidth}px`,
    height: `${posterHeight}px`,
  };
  const padding = props.isSmallScreen
    ? columnPaddingSmallScreen
    : columnPadding;
  const contentHeight = props.rowHeight - padding;
  const overviewHeight = contentHeight - TITLE_HEIGHT;

  return (
    <div className={styles.content}>
      <div className={styles.poster}>
        <div className={styles.posterContainer}>
          {isSelectMode ? <PosterSelect itemId={request.id} /> : null}

          <Link className={styles.link} style={elementStyle} to={link}>
            <RequestPoster
              style={elementStyle}
              url={request.media?.poster}
              lazy={false}
            />
          </Link>
        </div>

        <RequestProgressBar
          request={request}
          width={posterWidth}
          detailedProgressBar={options.detailedProgressBar}
        />
      </div>

      <div className={styles.info} style={{ maxHeight: contentHeight }}>
        <div className={styles.titleRow}>
          <Link className={styles.title} to={link}>
            {titleText(request)}
          </Link>

          <div className={styles.actions}>
            <OpenInLink media={request.media} />
          </div>
        </div>

        <div className={styles.details}>
          <Link className={styles.overview} to={link}>
            <TextTruncate
              line={Math.floor(overviewHeight / (defaultFontSize * lineHeight))}
              text={request.media?.overview ?? ''}
            />
          </Link>

          <RequestIndexOverviewInfo
            request={request}
            options={options}
            height={overviewHeight}
          />
        </div>
      </div>
    </div>
  );
}

export default RequestIndexOverview;
