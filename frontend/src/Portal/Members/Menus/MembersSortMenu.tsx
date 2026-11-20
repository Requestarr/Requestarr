import React from 'react';
import MenuContent from 'Components/Menu/MenuContent';
import SortMenu from 'Components/Menu/SortMenu';
import SortMenuItem from 'Components/Menu/SortMenuItem';
import { align } from 'Helpers/Props';
import SortDirection from 'Helpers/Props/SortDirection';
import translate from 'Utilities/String/translate';

// The same columns the table sorts by.
const SORTS: [string, string][] = [
  ['name', 'PortalMember'],
  ['status', 'PortalStatus'],
  ['requests', 'PortalRequests'],
  ['active', 'PortalActive'],
  ['pending', 'PortalPending'],
  ['following', 'PortalFollowing'],
  ['firstRequest', 'PortalFirstRequest'],
  ['lastRequest', 'PortalLastRequest'],
  ['added', 'PortalAdded'],
];

interface MembersSortMenuProps {
  sortKey: string;
  sortDirection: SortDirection;
  isDisabled: boolean;
  onSortSelect: (sortKey: string) => void;
}

// Prowlarr's IndexerIndexSortMenu, for members.
function MembersSortMenu(props: MembersSortMenuProps) {
  const { sortKey, sortDirection, isDisabled, onSortSelect } = props;

  return (
    <SortMenu isDisabled={isDisabled} alignMenu={align.RIGHT}>
      <MenuContent>
        {SORTS.map(([name, label]) => (
          <SortMenuItem
            key={name}
            name={name}
            sortKey={sortKey}
            sortDirection={sortDirection}
            onPress={onSortSelect}
          >
            {translate(label)}
          </SortMenuItem>
        ))}
      </MenuContent>
    </SortMenu>
  );
}

export default MembersSortMenu;
