import React from 'react';
import MenuContent from 'Components/Menu/MenuContent';
import ViewMenu from 'Components/Menu/ViewMenu';
import ViewMenuItem from 'Components/Menu/ViewMenuItem';
import { align } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

const VIEWS: [string, string][] = [
  ['table', 'Table'],
  ['posters', 'Posters'],
  ['overview', 'Overview'],
];

interface RequestsViewMenuProps {
  view: string;
  isDisabled: boolean;
  onViewSelect: (view: string) => void;
}

// Sonarr main's SeriesIndexViewMenu, for requests.
function RequestsViewMenu({
  view,
  isDisabled,
  onViewSelect,
}: RequestsViewMenuProps) {
  return (
    <ViewMenu isDisabled={isDisabled} alignMenu={align.RIGHT}>
      <MenuContent>
        {VIEWS.map(([name, label]) => (
          <ViewMenuItem
            key={name}
            name={name}
            selectedView={view}
            onPress={onViewSelect}
          >
            {translate(label)}
          </ViewMenuItem>
        ))}
      </MenuContent>
    </ViewMenu>
  );
}

export default RequestsViewMenu;
