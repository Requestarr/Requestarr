import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FilterMenu from 'Components/Menu/FilterMenu';
import { align } from 'Helpers/Props';
import { listActions } from 'Store/Actions/portalListActions';
import { ListActions } from '../../Components/PortalList';

// Both lists of requests on a member's page: theirs and those they follow.
const LISTS = ['memberRequests', 'memberFollowing'];

function selectList(state: AppState) {
  return state.portalLists[LISTS[0]];
}

// The Requests page's filters on a member's page, as Prowlarr's History has its filters: the server filters, so there
// are no custom ones.
function MemberRequestsFilterMenu() {
  const { selectedFilterKey, filters } = useSelector(selectList);
  const dispatch = useDispatch();
  const onFilterSelect = useCallback(
    (filterKey: string | number) => {
      LISTS.forEach((name) => {
        const { setFilter } = listActions[name] as unknown as ListActions;
        dispatch(setFilter({ selectedFilterKey: filterKey }));
      });
    },
    [dispatch]
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
