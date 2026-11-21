import React, { useCallback, useState } from 'react';
import styles from './Avatar.css';

// Discord's own picture for someone without one; also shown when a picture doesn't load, never a broken image.
const DEFAULT_AVATAR = 'https://cdn.discordapp.com/embed/avatars/0.png';

interface AvatarProps {
  url?: string | null;
  size?: 'small' | 'medium' | 'large' | 'header';
}

// Discord's picture addresses end with the size asked for (128 by default); a member's header asks for one larger than
// it shows, so it stays sharp.
const HEADER_PICTURE_SIZE = 256;

function sized(url: string, size: AvatarProps['size']) {
  return size === 'header'
    ? url.replace(/size=\d+/, `size=${HEADER_PICTURE_SIZE}`)
    : url;
}

// Someone's Discord picture, round, as Discord shows it.
function Avatar({ url, size = 'medium' }: AvatarProps) {
  // The address that didn't load: another one (the page shows someone else) is tried.
  const [failedUrl, setFailedUrl] = useState<string>();
  const onError = useCallback(() => setFailedUrl(url ?? undefined), [url]);

  return (
    <img
      className={styles[size]}
      src={url && url !== failedUrl ? sized(url, size) : DEFAULT_AVATAR}
      alt=""
      loading="lazy"
      onError={onError}
    />
  );
}

export default Avatar;
