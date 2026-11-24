import React from 'react';
import { CustomFilter, Filter } from 'App/State/AppState';
import FilterMenu from 'Components/Menu/FilterMenu';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Column from 'Components/Table/Column';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import { align, icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import AddMembersFilterModal from './AddMembersFilterModal';

interface AddMembersToolbarProps {
  columns: Column[];
  selectedFilterKey: string | number;
  filters: Filter[];
  customFilters: CustomFilter[];
  isFetching: boolean;
  isEmpty: boolean;
  hasPicks: boolean;
  onOnboardPress: () => void;
  onRefreshPress: () => void;
  onTableOptionChange: (payload: unknown) => void;
  onFilterSelect: (filterKey: string | number) => void;
}

// The Onboard page's toolbar, as Sonarr's Activity, then Queue lays its out: Refresh and what's done to the picks on the
// left; Options and Filter on the right.
function AddMembersToolbar(props: AddMembersToolbarProps) {
  return (
    <PageToolbar>
      <PageToolbarSection>
        <PageToolbarButton
          label={translate('Refresh')}
          iconName={icons.REFRESH}
          spinningName={icons.REFRESH}
          isSpinning={props.isFetching}
          onPress={props.onRefreshPress}
        />

        <PageToolbarSeparator />

        <PageToolbarButton
          label={translate('PortalActionOnboard')}
          iconName={icons.ADD}
          isDisabled={!props.hasPicks}
          onPress={props.onOnboardPress}
        />
      </PageToolbarSection>

      <PageToolbarSection alignContent={align.RIGHT} collapseButtons={false}>
        <TableOptionsModalWrapper
          columns={props.columns}
          onTableOptionChange={props.onTableOptionChange}
        >
          <PageToolbarButton
            label={translate('Options')}
            iconName={icons.TABLE}
          />
        </TableOptionsModalWrapper>

        <FilterMenu
          alignMenu={align.RIGHT}
          selectedFilterKey={props.selectedFilterKey}
          filters={props.filters}
          customFilters={props.customFilters}
          isDisabled={props.isEmpty}
          filterModalConnectorComponent={AddMembersFilterModal}
          onFilterSelect={props.onFilterSelect}
        />
      </PageToolbarSection>
    </PageToolbar>
  );
}

export default AddMembersToolbar;
