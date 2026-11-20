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
import RequestsFilterMenu from './Menus/RequestsFilterMenu';
import RequestsSortMenu from './Menus/RequestsSortMenu';
import RequestsViewMenu from './Menus/RequestsViewMenu';

interface RequestsToolbarProps {
  view: string;
  sortKey: string;
  sortDirection: SortDirection;
  selectedFilterKey: string | number;
  filters: Filter[];
  customFilters: CustomFilter[];
  columns: Column[];
  isFetching: boolean;
  selectActions: ToolbarAction[];
  isBusy: boolean;
  isSelectMode: boolean;
  isEmpty: boolean;
  onRefreshPress: () => void;
  onSelectModePress: () => void;
  onOptionsPress: () => void;
  onTableOptionChange: (payload: unknown) => void;
  onViewSelect: (view: string) => void;
  onSortSelect: (sortKey: string) => void;
  onFilterSelect: (filterKey: string | number) => void;
}

// The Requests page's toolbar, as Sonarr main's SeriesIndex and Prowlarr's IndexerIndex lay theirs out: Refresh and
// select mode on the left; Options (Table Options for the table, the view's own for posters and overview), View, Sort
// and Filter on the right. In select mode, what can be done to the picks comes first (the owner's choice over
// Servarr's bar along the bottom).
function RequestsToolbar(props: RequestsToolbarProps) {
  const { view, isSelectMode, isEmpty } = props;

  return (
    <PageToolbar>
      <PageToolbarSection>
        {selectActionButtons({
          actions: props.selectActions,
          isSelectMode,
          isBusy: props.isBusy,
        })}

        <PageToolbarButton
          label={translate('Refresh')}
          iconName={icons.REFRESH}
          isSpinning={props.isFetching}
          onPress={props.onRefreshPress}
        />

        <PageToolbarSeparator />

        <SelectModeButton
          label={
            isSelectMode
              ? translate('StopSelecting')
              : translate('PortalSelectRequests')
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
        {view === 'table' ? (
          <TableOptionsModalWrapper
            columns={props.columns}
            onTableOptionChange={props.onTableOptionChange}
          >
            <PageToolbarButton
              label={translate('Options')}
              iconName={icons.TABLE}
            />
          </TableOptionsModalWrapper>
        ) : (
          <PageToolbarButton
            label={translate('Options')}
            iconName={view === 'posters' ? icons.POSTER : icons.OVERVIEW}
            isDisabled={isEmpty}
            onPress={props.onOptionsPress}
          />
        )}

        <PageToolbarSeparator />

        <RequestsViewMenu
          view={view}
          isDisabled={isEmpty}
          onViewSelect={props.onViewSelect}
        />

        <RequestsSortMenu
          sortKey={props.sortKey}
          sortDirection={props.sortDirection}
          isDisabled={isEmpty}
          onSortSelect={props.onSortSelect}
        />

        <RequestsFilterMenu
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

export default RequestsToolbar;
