import React, { ReactNode } from 'react';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { PortalPerson } from '../PortalTypes';
import Person from './Person';
import styles from './MemberLink.css';

// A member's picture, name and @handle, opening their page.
function MemberLink({ person }: { person: PortalPerson }) {
  return (
    <Link to={`/members/${person.id}`}>
      <Person
        name={person.name ?? translate('PortalUnknownMember')}
        handle={person.handle}
        avatar={person.avatar}
      />
    </Link>
  );
}

interface MemberLinksProps {
  people: PortalPerson[];
  before?: ReactNode;
}

// Several members side by side, after what introduces them ("Followed by").
export function MemberLinks({ people, before }: MemberLinksProps) {
  return (
    <div className={styles.list}>
      {before}
      {people.map((person) => (
        <MemberLink key={person.id} person={person} />
      ))}
    </div>
  );
}

export default MemberLink;
