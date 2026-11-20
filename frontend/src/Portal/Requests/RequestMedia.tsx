import moment from 'moment';
import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalMedia } from '../PortalTypes';

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
