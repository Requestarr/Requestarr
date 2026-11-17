import React from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalPerson } from '../PortalTypes';

// A member's name, opening their page.
function MemberLink({ person }: { person: PortalPerson }) {
  return (
    <Link to={`/members/${person.id}`}>
      {person.name ?? translate('PortalUnknownMember')}
    </Link>
  );
}

export default MemberLink;
