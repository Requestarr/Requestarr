import classNames from 'classnames';
import React, { useCallback } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import Person from '../Components/Person';
import { PortalLibraryTitle } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import RequestPoster from '../Requests/RequestPoster';
import { SearchItem } from './usePortalSearch';
import styles from './PortalSearch.css';

// Start removal (isForce false) or Force removal on a library title nobody requested here.
type OnLibraryRemove = (title: PortalLibraryTitle, isForce: boolean) => void;

interface ResultProps {
  item: SearchItem;
  isHighlighted: boolean;
  onOpen: () => void;
  onRemove: OnLibraryRemove;
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

interface LibraryResultProps {
  title: PortalLibraryTitle;
  isHighlighted: boolean;
  onRemove: OnLibraryRemove;
}

// A library title nobody requested here: its poster and where it is, and its removal right in the result.
function LibraryResult({ title, isHighlighted, onRemove }: LibraryResultProps) {
  const onStart = useCallback(() => onRemove(title, false), [title, onRemove]);
  const onForce = useCallback(() => onRemove(title, true), [title, onRemove]);

  return (
    <div className={styles.resultRow}>
      <div
        className={classNames(
          styles.result,
          isHighlighted && styles.highlighted
        )}
      >
        <RequestPoster url={title.poster} className={styles.poster} />
        <span className={styles.label}>{title.label}</span>
        <span className={styles.detail}>{title.instance}</span>
      </div>
      <span className={styles.open}>
        <Link className={styles.removal} onPress={onStart}>
          {actionLabel('remove')}
        </Link>
        <Link className={styles.removal} onPress={onForce}>
          {actionLabel('forceRemoval')}
        </Link>
      </span>
    </div>
  );
}

// A request also shows its poster and who asked for it.
function Result({ item, isHighlighted, onOpen, onRemove }: ResultProps) {
  if (item.library) {
    return (
      <LibraryResult
        title={item.library}
        isHighlighted={isHighlighted}
        onRemove={onRemove}
      />
    );
  }

  return (
    <div className={styles.resultRow}>
      <Link
        className={classNames(
          styles.result,
          isHighlighted && styles.highlighted
        )}
        to={item.path ?? undefined}
        onPress={onOpen}
      >
        <ResultLabel item={item} />
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
    </div>
  );
}

interface PortalSearchResultsProps {
  items: SearchItem[];
  highlight: number;
  onOpen: () => void;
  onRemove: OnLibraryRemove;
}

// The results under the header search, grouped by kind, the highlighted one opened by Enter.
function PortalSearchResults({
  items,
  highlight,
  onOpen,
  onRemove,
}: PortalSearchResultsProps) {
  return (
    <div className={styles.results}>
      {!items.length && (
        <div className={styles.nothing}>{translate('PortalNothingFound')}</div>
      )}

      {items.map((item, index) => (
        <React.Fragment
          key={
            item.path ?? `${item.library?.instanceId}-${item.library?.itemId}`
          }
        >
          {(index === 0 || items[index - 1].group !== item.group) && (
            <div className={styles.group}>{item.group}</div>
          )}
          <Result
            item={item}
            isHighlighted={index === highlight}
            onOpen={onOpen}
            onRemove={onRemove}
          />
        </React.Fragment>
      ))}
    </div>
  );
}

export default PortalSearchResults;
