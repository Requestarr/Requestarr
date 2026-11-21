import React, { ReactNode } from 'react';
import styles from './DetailsHeader.css';

interface DetailsHeaderProps {
  // The picture behind the header, dimmed; none leaves it dark.
  backdropUrl?: string | null;

  // The poster (a request) or picture (a member) at the left.
  image: ReactNode;
  title: ReactNode;

  // A line under the title: what it is and when it came out.
  details?: ReactNode;
  labels: ReactNode;
  overview?: string | null;
}

// A request's or member's page header, as Sonarr main's Series/Details/SeriesDetails draws a series: the dimmed
// backdrop, the poster, the title, a details line, the label chips and the overview.
function DetailsHeader(props: DetailsHeaderProps) {
  const { backdropUrl, image, title, details, labels, overview } = props;

  return (
    <div className={styles.header}>
      <div
        className={styles.backdrop}
        style={backdropUrl ? { backgroundImage: `url(${backdropUrl})` } : {}}
      >
        <div className={styles.backdropOverlay} />
      </div>

      <div className={styles.headerContent}>
        <div className={styles.poster}>{image}</div>

        <div className={styles.info}>
          <div className={styles.title}>{title}</div>
          {details ? <div className={styles.details}>{details}</div> : null}
          <div className={styles.detailsLabels}>{labels}</div>
          {overview ? <div className={styles.overview}>{overview}</div> : null}
        </div>
      </div>
    </div>
  );
}

export const detailsStyles = styles;

export default DetailsHeader;
