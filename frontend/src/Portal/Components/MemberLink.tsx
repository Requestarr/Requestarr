import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalPerson } from '../PortalTypes';
import Person, { PersonSize } from './Person';

interface MemberLinkProps {
  person: PortalPerson;
  size?: PersonSize;
}

// A member's picture, name and @handle (small: picture and name, the @handle on hover), opening their page.
function MemberLink({ person, size }: MemberLinkProps) {
  return (
    <Link to={`/members/${person.id}`}>
      <Person
        name={person.name ?? translate('PortalUnknownMember')}
        handle={person.handle}
        avatar={person.avatar}
        size={size}
      />
    </Link>
  );
}

export default MemberLink;
