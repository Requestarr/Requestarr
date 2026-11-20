import React, { useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FilterModal from 'Components/Filter/FilterModal';
import { setRequestFilter } from 'Store/Actions/requestIndexActions';

function selectRequests(state: AppState) {
  return state.requests.items;
}

function selectFilterBuilderProps(state: AppState) {
  return state.requestIndex.filterBuilderProps;
}

interface RequestsFilterModalProps {
  isOpen: boolean;
}

// The custom filter builder for the Requests page, as Prowlarr's IndexerIndexFilterModal is for its indexers.
function RequestsFilterModal(props: RequestsFilterModalProps) {
  const sectionItems = useSelector(selectRequests);
  const filterBuilderProps = useSelector(selectFilterBuilderProps);
  const dispatch = useDispatch();

  const dispatchSetFilter = useCallback(
    (payload: unknown) => {
      dispatch(setRequestFilter(payload));
    },
    [dispatch]
  );

  return (
    <FilterModal
      {...props}
      sectionItems={sectionItems}
      filterBuilderProps={filterBuilderProps}
      customFilterType="requestIndex"
      dispatchSetFilter={dispatchSetFilter}
    />
  );
}

export default RequestsFilterModal;
