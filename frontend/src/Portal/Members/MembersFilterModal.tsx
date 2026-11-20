import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FilterModal from 'Components/Filter/FilterModal';
import { setMemberFilter } from 'Store/Actions/memberIndexActions';

function selectMembers(state: AppState) {
  return state.members.items;
}

function selectFilterBuilderProps(state: AppState) {
  return state.memberIndex.filterBuilderProps;
}

interface MembersFilterModalProps {
  isOpen: boolean;
}

// The custom filter builder for the Members page, as Prowlarr's IndexerIndexFilterModal is for its indexers.
function MembersFilterModal(props: MembersFilterModalProps) {
  const sectionItems = useSelector(selectMembers);
  const filterBuilderProps = useSelector(selectFilterBuilderProps);
  const dispatch = useDispatch();

  const dispatchSetFilter = useCallback(
    (payload: unknown) => {
      dispatch(setMemberFilter(payload));
    },
    [dispatch]
  );

  return (
    <FilterModal
      {...props}
      sectionItems={sectionItems}
      filterBuilderProps={filterBuilderProps}
      customFilterType="memberIndex"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}

export default MembersFilterModal;
