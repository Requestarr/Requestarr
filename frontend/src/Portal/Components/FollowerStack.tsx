import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalPerson } from '../PortalTypes';
import Avatar from './Avatar';
import styles from './FollowerStack.css';

// The stack shows this many pictures; the rest are a count.
const SHOWN_PICTURES = 5;

// Hovering lists this many names, then how many more.
const LISTED_NAMES = 10;

// The hover's list: a name a line, then how many more there are.
function followerNames(followers: PortalPerson[]) {
  const names = followers
    .slice(0, LISTED_NAMES)
    .map((person) => person.name ?? translate('PortalUnknownMember'));
  const more = followers.length - names.length;

  return (
    more > 0 ? [...names, translate('PortalAndMore', { count: more })] : names
  ).join('\n');
}

interface FollowersProps {
  requestId: number;
  followers: PortalPerson[];
}

// The request's page at its Followers section, which lists everyone; hovering lists the first few names.
function FollowersLink({
  requestId,
  followers,
  className,
  children,
}: FollowersProps & { className: string; children: React.ReactNode }) {
  return (
    <Link
      className={className}
      to={`/requests/${requestId}#followers`}
      title={followerNames(followers)}
    >
      {children}
    </Link>
  );
}

// A request's followers in a list, one line however many there are: up to five overlapping pictures, then "3
// following", or "+37 following" for the rest beyond the five. Nothing when nobody follows it.
function FollowerStack({ requestId, followers }: FollowersProps) {
  if (!followers.length) {
    return null;
  }

  const rest = followers.length - SHOWN_PICTURES;

  return (
    <FollowersLink
      className={styles.stack}
      requestId={requestId}
      followers={followers}
    >
      <span className={styles.pictures}>
        {followers.slice(0, SHOWN_PICTURES).map((person) => (
          <Avatar key={person.id} url={person.avatar} size="small" />
        ))}
      </span>
      <span className={styles.count}>
        {rest > 0
          ? translate('PortalMoreFollowing', { count: rest })
          : translate('PortalCountFollowing', { count: followers.length })}
      </span>
    </FollowersLink>
  );
}

export default FollowerStack;
