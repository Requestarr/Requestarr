import React from 'react';
import { CustomFilter, Filter } from 'App/State/AppState';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Column from 'Components/Table/Column';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import { align, icons } from 'Helpers/Props';
import SortDirection from 'Helpers/Props/SortDirection';
import translate from 'Utilities/String/translate';
import selectActionButtons from '../Components/Select/selectActionButtons';
import { ToolbarAction } from '../Components/Select/selectActions';
import SelectAllButton from '../Components/Select/SelectAllButton';
import SelectAllMenuItem from '../Components/Select/SelectAllMenuItem';
import SelectModeButton from '../Components/Select/SelectModeButton';
import SelectModeMenuItem from '../Components/Select/SelectModeMenuItem';
import MembersFilterMenu from './Menus/MembersFilterMenu';
import MembersSortMenu from './Menus/MembersSortMenu';

interface MembersToolbarProps {
  sortKey: string;
  sortDirection: SortDirection;
  selectedFilterKey: string | number;
  filters: Filter[];
  customFilters: CustomFilter[];
  columns: Column[];
  isSelectMode: boolean;
  selectActions: ToolbarAction[];
  isBusy: boolean;
  isEmpty: boolean;
  onAddPress: () => void;
  onSelectModePress: () => void;
  onTableOptionChange: (payload: unknown) => void;
  onSortSelect: (sortKey: string) => void;
  onFilterSelect: (filterKey: string | number) => void;
}

// The Members page's toolbar, as Prowlarr's IndexerIndex lays its out: Onboard and select mode on the left; Options, Sort
// and Filter on the right. In select mode, what can be done to the picks comes first, left of Onboard (the owner's
// choice over Servarr's bar along the bottom).
function MembersToolbar(props: MembersToolbarProps) {
  const { isSelectMode, isEmpty } = props;

  return (
    <PageToolbar>
      <PageToolbarSection>
        {selectActionButtons({
          actions: props.selectActions,
          isSelectMode,
          isBusy: props.isBusy,
        })}

        <PageToolbarButton
          label={translate('PortalActionOnboard')}
          iconName={icons.ADD}
          onPress={props.onAddPress}
        />

        <PageToolbarSeparator />

        <SelectModeButton
          label={
            isSelectMode
              ? translate('StopSelecting')
              : translate('PortalSelectMembers')
          }
          iconName={isSelectMode ? icons.SERIES_ENDED : icons.CHECK}
          isSelectMode={isSelectMode}
          overflowComponent={SelectModeMenuItem}
          onPress={props.onSelectModePress}
        />

        <SelectAllButton
          label={translate('SelectAll')}
          isSelectMode={isSelectMode}
          overflowComponent={SelectAllMenuItem}
        />
      </PageToolbarSection>

      <PageToolbarSection alignContent={align.RIGHT} collapseButtons={false}>
        <TableOptionsModalWrapper
          columns={props.columns}
          canModifyColumns={true}
          onTableOptionChange={props.onTableOptionChange}
        >
          <PageToolbarButton
            label={translate('Options')}
            iconName={icons.TABLE}
          />
        </TableOptionsModalWrapper>

        <PageToolbarSeparator />

        <MembersSortMenu
          sortKey={props.sortKey}
          sortDirection={props.sortDirection}
          isDisabled={isEmpty}
          onSortSelect={props.onSortSelect}
        />

        <MembersFilterMenu
          selectedFilterKey={props.selectedFilterKey}
          filters={props.filters}
          customFilters={props.customFilters}
          isDisabled={isEmpty}
          onFilterSelect={props.onFilterSelect}
        />
      </PageToolbarSection>
    </PageToolbar>
  );
}

export default MembersToolbar;
