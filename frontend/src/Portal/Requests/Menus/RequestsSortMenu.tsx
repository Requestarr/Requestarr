import React from 'react';
import MenuContent from 'Components/Menu/MenuContent';
import SortMenu from 'Components/Menu/SortMenu';
import SortMenuItem from 'Components/Menu/SortMenuItem';
import { align } from 'Helpers/Props';
import SortDirection from 'Helpers/Props/SortDirection';
import translate from 'Utilities/String/translate';

// The same columns the table sorts by.
const SORTS: [string, string][] = [
  ['title', 'PortalTitle'],
  ['type', 'PortalType'],
  ['status', 'PortalStatus'],
  ['requester', 'PortalRequester'],
  ['followers', 'PortalFollowers'],
  ['instance', 'PortalInstance'],
  ['requested', 'PortalRequested'],
  ['decided', 'PortalDecided'],
  ['fulfilled', 'PortalFulfilled'],
];

interface RequestsSortMenuProps {
  sortKey: string;
  sortDirection: SortDirection;
  isDisabled: boolean;
  onSortSelect: (sortKey: string) => void;
}

// Sonarr main's SeriesIndexSortMenu, for requests.
function RequestsSortMenu(props: RequestsSortMenuProps) {
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

export default RequestsSortMenu;
