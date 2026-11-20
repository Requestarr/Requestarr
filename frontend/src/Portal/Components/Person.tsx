import classNames from 'classnames';
import React, { ReactNode } from 'react';
import Avatar from './Avatar';
import styles from './Person.css';

export type PersonSize = 'small' | 'medium' | 'large';

interface PersonProps {
  name: ReactNode;
  handle?: string | null;
  avatar?: string | null;
  size?: PersonSize;
  // The @handle's colour where the page's grey doesn't suit, as on the header's colour.
  handleClassName?: string;
}

// Someone's picture on the left, as tall as their name and @handle together, both to its right. Small, for a row
// that names them in passing, is the picture and name on one line, the @handle shown on hover.
function Person({
  name,
  handle,
  avatar,
  size = 'medium',
  handleClassName,
}: PersonProps) {
  const isSmall = size === 'small';

  return (
    <span
      className={styles[size]}
      title={isSmall && handle ? `@${handle}` : undefined}
    >
      <Avatar url={avatar} size={size} />
      <span className={styles.lines}>
        <span className={styles.name}>{name}</span>
        {handle && !isSmall && (
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
