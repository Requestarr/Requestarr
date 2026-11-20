import classNames from 'classnames';
import React, { ReactNode } from 'react';
import Avatar from './Avatar';
import styles from './Person.css';

interface PersonProps {
  name: ReactNode;
  handle?: string | null;
  avatar?: string | null;
  size?: 'medium' | 'large';
  // The @handle's colour where the page's grey doesn't suit, as on the header's colour.
  handleClassName?: string;
}

// Someone's picture on the left, as tall as their name and @handle together, both to its right.
function Person({
  name,
  handle,
  avatar,
  size = 'medium',
  handleClassName,
}: PersonProps) {
  return (
    <span className={styles[size]}>
      <Avatar url={avatar} size={size} />
      <span className={styles.lines}>
        <span className={styles.name}>{name}</span>
        {handle && (
          <span
            className={classNames(
              styles.handle,
              handleClassName ?? styles.muted
            )}
          >
            @{handle}
          </span>
        )}
      </span>
    </span>
  );
}

export default Person;
