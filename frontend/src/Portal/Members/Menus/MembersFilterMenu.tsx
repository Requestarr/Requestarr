import React from 'react';
import FilterMenu from 'Components/Menu/FilterMenu';
import { align } from 'Helpers/Props';
import { RequestsFilterMenuProps } from '../../Requests/Menus/RequestsFilterMenu';
import MembersFilterModal from '../MembersFilterModal';

// Prowlarr's IndexerIndexFilterMenu, for members: the filters, the custom filters and the filter builder.
function MembersFilterMenu(props: RequestsFilterMenuProps) {
  return (
    <FilterMenu
      {...props}
      alignMenu={align.RIGHT}
      filterModalConnectorComponent={MembersFilterModal}
    />
  );
}

export default MembersFilterMenu;
