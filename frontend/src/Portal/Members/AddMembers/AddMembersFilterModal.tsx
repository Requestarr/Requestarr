import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FilterModal from 'Components/Filter/FilterModal';
import { setServerMemberFilter } from 'Store/Actions/serverMemberActions';

function selectServerMembers(state: AppState) {
  return state.serverMembers.items;
}

function selectFilterBuilderProps(state: AppState) {
  return state.serverMembers.filterBuilderProps;
}

interface AddMembersFilterModalProps {
  isOpen: boolean;
}

// The custom filter builder for the Onboard page, as Prowlarr's IndexerIndexFilterModal is for its indexers.
function AddMembersFilterModal(props: AddMembersFilterModalProps) {
  const sectionItems = useSelector(selectServerMembers);
  const filterBuilderProps = useSelector(selectFilterBuilderProps);
  const dispatch = useDispatch();

  const dispatchSetFilter = useCallback(
    (payload: unknown) => {
      dispatch(setServerMemberFilter(payload));
    },
    [dispatch]
  );

  return (
    <FilterModal
      {...props}
      sectionItems={sectionItems}
      filterBuilderProps={filterBuilderProps}
      customFilterType="serverMembers"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}

export default AddMembersFilterModal;
