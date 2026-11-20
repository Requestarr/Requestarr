import classNames from 'classnames';
import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import Person from '../Components/Person';
import { OpenInLink, RequestPoster } from '../Requests/RequestMedia';
import { SearchItem } from './usePortalSearch';
import styles from './PortalSearch.css';

interface ResultProps {
  item: SearchItem;
  isHighlighted: boolean;
  onOpen: () => void;
}

// A member with their picture and @handle, or a request with its poster.
function ResultLabel({ item }: { item: SearchItem }) {
  if (item.person) {
    return (
      <span className={styles.label}>
        <Person
          name={item.label}
          handle={item.person.handle}
          avatar={item.person.avatar}
          handleClassName={styles.handle}
        />
      </span>
    );
  }

  return (
    <>
      {item.media && (
        <RequestPoster url={item.media.poster} className={styles.poster} />
      )}
      <span className={styles.label}>{item.label}</span>
    </>
  );
}

// A request also shows its poster, and its page in Sonarr or Radarr beside it when a browser can reach that.
function Result({ item, isHighlighted, onOpen }: ResultProps) {
  return (
    <div className={styles.resultRow}>
      <Link
        className={classNames(
          styles.result,
          isHighlighted && styles.highlighted
        )}
        to={item.path}
        onPress={onOpen}
      >
        <ResultLabel item={item} />
        {item.detail && <span className={styles.detail}>{item.detail}</span>}
        {item.requester && (
          <span className={styles.detail}>
            <Person
              name={item.requester.name ?? translate('PortalUnknownMember')}
              avatar={item.requester.avatar}
              size="small"
            />
          </span>
        )}
      </Link>
      {item.media?.openUrl && (
        <span className={styles.open}>
          <OpenInLink media={item.media} />
        </span>
      )}
    </div>
  );
}

interface PortalSearchResultsProps {
  items: SearchItem[];
  highlight: number;
  onOpen: () => void;
}

// The results under the header search, grouped by kind, the highlighted one opened by Enter.
function PortalSearchResults({
  items,
  highlight,
  onOpen,
}: PortalSearchResultsProps) {
  return (
    <div className={styles.results}>
      {!items.length && (
        <div className={styles.nothing}>{translate('PortalNothingFound')}</div>
      )}

      {items.map((item, index) => (
        <React.Fragment key={item.path}>
          {(index === 0 || items[index - 1].group !== item.group) && (
            <div className={styles.group}>{item.group}</div>
          )}
          <Result
            item={item}
            isHighlighted={index === highlight}
            onOpen={onOpen}
          />
        </React.Fragment>
      ))}
    </div>
  );
}

export default PortalSearchResults;
