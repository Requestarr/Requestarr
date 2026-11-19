import moment from 'moment';
import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalMedia } from '../PortalTypes';

interface RequestPosterProps {
  url?: string | null;
  className: string;
}

// The title's poster; nothing at all when there isn't one or it doesn't load, never a broken image.
export function RequestPoster({ url, className }: RequestPosterProps) {
  const [failed, setFailed] = useState(false);
  const onError = useCallback(() => setFailed(true), []);

  if (!url || failed) {
    return null;
  }

  return (
    <img
      className={className}
      src={url}
      alt=""
      loading="lazy"
      onError={onError}
    />
  );
}

function dated(key: string, date?: string | null) {
  return date
    ? translate(key, { date: moment.utc(date).format('MMM D, YYYY') })
    : null;
}

// "In theaters Nov 7, 2025 · Digital Dec 1, 2025" for a movie, in date order; "First aired Jan 20, 2008 · AMC" for a
// show.
export function releaseText(media?: PortalMedia | null) {
  if (!media) {
    return '';
  }

  const releases: [string, string | null | undefined][] = [
    ['PortalInTheaters', media.inCinemas],
    ['PortalDigitalRelease', media.digitalRelease],
    ['PortalPhysicalRelease', media.physicalRelease],
    ['PortalFirstAired', media.firstAired],
  ];

  return [
    ...releases
      .filter(([, date]) => date)
      .sort(([, a], [, b]) => String(a).localeCompare(String(b)))
      .map(([key, date]) => dated(key, date)),
    media.network,
  ]
    .filter(Boolean)
    .join(' · ');
}

// "Open in Sonarr": the title's page there, when it's in a Sonarr or Radarr a browser can reach.
export function OpenInLink({ media }: { media?: PortalMedia | null }) {
  if (!media?.openUrl || !media.openIn) {
    return null;
  }

  return (
    <Link to={media.openUrl}>
      {translate('PortalOpenIn', { app: media.openIn })}
    </Link>
  );
}
