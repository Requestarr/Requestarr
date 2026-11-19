import React, { useCallback, useState } from 'react';
import styles from './Avatar.css';

// Discord's own picture for someone without one; also shown when a picture doesn't load, never a broken image.
const DEFAULT_AVATAR = 'https://cdn.discordapp.com/embed/avatars/0.png';

interface AvatarProps {
  url?: string | null;
  size?: 'small' | 'large';
}

// Someone's Discord picture, round, as Discord shows it.
function Avatar({ url, size = 'small' }: AvatarProps) {
  const [failed, setFailed] = useState(false);
  const onError = useCallback(() => setFailed(true), []);

  return (
    <img
      className={styles[size]}
      src={url && !failed ? url : DEFAULT_AVATAR}
      alt=""
      loading="lazy"
      onError={onError}
    />
  );
}

export default Avatar;
