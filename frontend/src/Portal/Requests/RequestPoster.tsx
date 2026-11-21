import React, { CSSProperties, useCallback, useEffect, useState } from 'react';

const placeholder = `${window.Requestarr.urlBase}/Content/Images/poster-dark.png`;

interface RequestPosterProps {
  className?: string;
  style?: CSSProperties;
  url?: string | null;
  lazy?: boolean;
  onError?: () => void;
  onLoad?: () => void;
}

// A title's poster, as Sonarr main's Series/SeriesPoster and SeriesImage draw one: the placeholder while there's none
// or it doesn't load, never a broken image. A request has one poster address, not Sonarr's list of sized images.
function RequestPoster(props: RequestPosterProps) {
  const { className, style, url, lazy = true, onError, onLoad } = props;
  // The address that failed to load, so a new one is tried again.
  const [failedUrl, setFailedUrl] = useState<string>();

  useEffect(() => {
    if (!url) {
      onError?.();
    }
  }, [url, onError]);

  const handleError = useCallback(() => {
    setFailedUrl(url ?? undefined);
    onError?.();
  }, [url, onError]);

  if (!url || failedUrl === url) {
    return <img className={className} style={style} src={placeholder} alt="" />;
  }

  return (
    <img
      className={className}
      style={style}
      src={url}
      alt=""
      loading={lazy ? 'lazy' : 'eager'}
      onError={handleError}
      onLoad={onLoad}
    />
  );
}

export default RequestPoster;
