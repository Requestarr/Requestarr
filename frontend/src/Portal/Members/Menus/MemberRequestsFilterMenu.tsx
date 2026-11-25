import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FilterMenu from 'Components/Menu/FilterMenu';
import { align } from 'Helpers/Props';
import { listActions } from 'Store/Actions/portalListActions';
import { ListActions } from '../../Components/PortalList';

interface MemberRequestsFilterMenuProps {
  // The lists of requests it filters, all the same way.
  lists: string[];
}

// The Requests page's filters on a member's page, as Prowlarr's History has its filters: the server filters, so there
// are no custom ones.
function MemberRequestsFilterMenu({ lists }: MemberRequestsFilterMenuProps) {
  const { selectedFilterKey, filters } = useSelector(
    (state: AppState) => state.portalLists[lists[0]]
  );
  const dispatch = useDispatch();
  const onFilterSelect = useCallback(
    (filterKey: string | number) => {
      lists.forEach((name) => {
        const { setFilter } = listActions[name] as unknown as ListActions;
        dispatch(setFilter({ selectedFilterKey: filterKey }));
      });
    },
    [lists, dispatch]
  );

  return (
    <FilterMenu
      alignMenu={align.RIGHT}
      selectedFilterKey={selectedFilterKey}
      filters={filters}
      customFilters={[]}
      onFilterSelect={onFilterSelect}
    />
  );
}

export default MemberRequestsFilterMenu;
