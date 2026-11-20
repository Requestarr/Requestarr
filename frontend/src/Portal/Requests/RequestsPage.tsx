import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { SelectProvider } from 'App/SelectContext';
import ClientSideCollectionAppState from 'App/State/ClientSideCollectionAppState';
import {
  RequestIndexAppState,
  RequestsAppState,
} from 'App/State/PortalAppState';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import TablePager from 'Components/Table/TablePager';
import { kinds } from 'Helpers/Props';
import { fetchRequests } from 'Store/Actions/requestActions';
import {
  setRequestFilter,
  setRequestSort,
  setRequestTableOption,
  setRequestView,
} from 'Store/Actions/requestIndexActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import useClientSidePage from '../Components/Paging/useClientSidePage';
import { RequestsIndexTable } from '../Components/RequestsTable';
import { PortalRequest } from '../PortalTypes';
import RequestsFooter from './RequestsFooter';
import RequestsOverview from './RequestsOverview';
import RequestsPosters from './RequestsPosters';
import RequestsToolbar from './RequestsToolbar';
import useRequestActionRunner from './useRequestActionRunner';
import useRequestBulk from './useRequestBulk';

type RequestsState = RequestsAppState &
  RequestIndexAppState &
  ClientSideCollectionAppState;

// The store's requests, filtered and sorted by the page's own choices, with those choices.
function useRequests(): RequestsState {
  const selector = useMemo(
    () => createClientSideCollectionSelector('requests', 'requestIndex'),
    []
  );

  return useSelector(selector);
}

// What the toolbar changes in the store, as Prowlarr's IndexerIndex dispatches them.
function useIndexDispatch() {
  const dispatch = useDispatch();

  return useMemo(
    () => ({
      onRefreshPress: () => dispatch(fetchRequests()),
      onTableOptionChange: (payload: unknown) =>
        dispatch(setRequestTableOption(payload)),
      onViewSelect: (view: string) => dispatch(setRequestView({ view })),
      onSortSelect: (sortKey: string) => dispatch(setRequestSort({ sortKey })),
      onFilterSelect: (selectedFilterKey: string | number) =>
        dispatch(setRequestFilter({ selectedFilterKey })),
    }),
    [dispatch]
  );
}

interface ShownProps {
  state: RequestsState;
  requests: PortalRequest[];
  isSelectMode: boolean;
  onSortSelect: (sortKey: string) => void;
  onTableOptionChange: (payload: unknown) => void;
}

// The page of requests, in the view chosen.
function Shown(props: ShownProps) {
  const { state, requests, isSelectMode } = props;

  switch (state.view) {
    case 'overview':
      return (
        <RequestsOverview requests={requests} isSelectMode={isSelectMode} />
      );
    case 'posters':
      return (
        <RequestsPosters requests={requests} isSelectMode={isSelectMode} />
      );
    default:
      return (
        <RequestsIndexTable
          requests={requests}
          columns={state.columns}
          sort={{
            sortKey: state.sortKey,
            sortDirection: state.sortDirection,
            onSortPress: props.onSortSelect,
          }}
          isSelectMode={isSelectMode}
          onTableOptionChange={props.onTableOptionChange}
        />
      );
  }
}

interface RequestsBodyProps {
  state: RequestsState;
  isSelectMode: boolean;
  onSortSelect: (sortKey: string) => void;
  onTableOptionChange: (payload: unknown) => void;
}

function RequestsBody(props: RequestsBodyProps) {
  const { state } = props;
  const { pageItems, pager } = useClientSidePage(
    state.items,
    state.pageSize,
    JSON.stringify([
      state.selectedFilterKey,
      state.customFilters,
      state.sortKey,
      state.sortDirection,
    ])
  );

  if (!state.items.length) {
    return (
      <Alert kind={kinds.INFO}>
        {translate(
          state.totalItems ? 'PortalNoRequestsMatch' : 'PortalNoRequests'
        )}
      </Alert>
    );
  }

  return (
    <>
      <Shown {...props} requests={pageItems} />
      <TablePager {...pager} />
      <RequestsFooter requests={state.items} />
    </>
  );
}

// Every request, in the Table, Posters or Overview view, filtered, sorted and paged as Sonarr's series list is. Select
// mode picks one or several; what can be done to them is in the toolbar, left of Refresh.
function RequestsIndex() {
  const state = useRequests();
  const handlers = useIndexDispatch();
  const { onRefreshPress } = handlers;
  const [isSelectMode, setIsSelectMode] = useState(false);
  const runner = useRequestActionRunner(onRefreshPress);
  const bulk = useRequestBulk(state.items, runner.run, onRefreshPress);
  const onSelectModePress = useCallback(
    () => setIsSelectMode((current) => !current),
    []
  );

  useEffect(() => {
    onRefreshPress();
  }, [onRefreshPress]);

  return (
    <PageContent title={translate('PortalRequests')}>
      <RequestsToolbar
        {...state}
        {...handlers}
        selectActions={bulk.actions}
        isBusy={runner.isBusy || bulk.isBusy}
        isSelectMode={isSelectMode}
        isEmpty={!state.totalItems}
        onSelectModePress={onSelectModePress}
      />

      <PageContentBody>
        {state.isFetching && !state.isPopulated && <LoadingIndicator />}

        {!state.isFetching && !!state.error && (
          <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
        )}

        <ActionOutcomeAlert outcome={runner.outcome} />

        {state.isPopulated && !state.error && (
          <RequestsBody
            {...handlers}
            state={state}
            isSelectMode={isSelectMode}
          />
        )}
      </PageContentBody>
      {runner.dialogs}
      {bulk.modal}
    </PageContent>
  );
}

// The page's select state, for its rows and its toolbar.
function RequestsPage() {
  const state = useRequests();

  return (
    <SelectProvider items={state.items}>
      <RequestsIndex />
    </SelectProvider>
  );
}

export default RequestsPage;
