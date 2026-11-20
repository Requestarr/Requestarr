import React from 'react';
import { CustomFilter, Filter } from 'App/State/AppState';
import FilterMenu from 'Components/Menu/FilterMenu';
import { align } from 'Helpers/Props';
import RequestsFilterModal from '../RequestsFilterModal';

export interface RequestsFilterMenuProps {
  selectedFilterKey: string | number;
  filters: Filter[];
  customFilters: CustomFilter[];
  isDisabled: boolean;
  onFilterSelect: (filterKey: string | number) => void;
}

// Sonarr main's SeriesIndexFilterMenu, for requests: the filters, the custom filters and the filter builder.
function RequestsFilterMenu(props: RequestsFilterMenuProps) {
  return (
    <FilterMenu
      {...props}
      alignMenu={align.RIGHT}
      filterModalConnectorComponent={RequestsFilterModal}
    />
  );
}

export default RequestsFilterMenu;
