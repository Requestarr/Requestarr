import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalPerson } from '../PortalTypes';
import Avatar from './Avatar';

// A member's picture and name, opening their page.
function MemberLink({ person }: { person: PortalPerson }) {
  return (
    <Link to={`/members/${person.id}`}>
      <Avatar url={person.avatar} />
      {person.name ?? translate('PortalUnknownMember')}
    </Link>
  );
}

export default MemberLink;
