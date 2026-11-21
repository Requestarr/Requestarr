import React, { useCallback, useState } from 'react';
import styles from './Avatar.css';

// Discord's own picture for someone without one; also shown when a picture doesn't load, never a broken image.
const DEFAULT_AVATAR = 'https://cdn.discordapp.com/embed/avatars/0.png';

interface AvatarProps {
  url?: string | null;
  size?: 'small' | 'medium' | 'large' | 'header';
}

// Someone's Discord picture, round, as Discord shows it.
function Avatar({ url, size = 'medium' }: AvatarProps) {
  // The address that didn't load: another one (the page shows someone else) is tried.
  const [failedUrl, setFailedUrl] = useState<string>();
  const onError = useCallback(() => setFailedUrl(url ?? undefined), [url]);

  return (
    <img
      className={styles[size]}
      src={url && url !== failedUrl ? url : DEFAULT_AVATAR}
      alt=""
      loading="lazy"
      onError={onError}
    />
  );
}

export default Avatar;
