import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useHistory } from 'react-router-dom';
import { SelectProvider, useSelect } from 'App/SelectContext';
import ClientSideCollectionAppState from 'App/State/ClientSideCollectionAppState';
import { MemberIndexAppState, MembersAppState } from 'App/State/PortalAppState';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import TablePager from 'Components/Table/TablePager';
import { kinds } from 'Helpers/Props';
import { fetchMembers } from 'Store/Actions/memberActions';
import {
  setMemberFilter,
  setMemberSort,
  setMemberTableOption,
} from 'Store/Actions/memberIndexActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import useClientSidePage from '../Components/Paging/useClientSidePage';
import { PortalMember } from '../PortalTypes';
import MembersFooter from './MembersFooter';
import MembersSelectFooter from './MembersSelectFooter';
import MembersTable from './MembersTable';
import MembersToolbar from './MembersToolbar';
import useMemberActions from './useMemberActions';

type MembersState = MembersAppState &
  MemberIndexAppState &
  ClientSideCollectionAppState;

// The store's members, filtered and sorted by the page's own choices, with those choices.
function useMembers(): MembersState {
  const selector = useMemo(
    () => createClientSideCollectionSelector('members', 'memberIndex'),
    []
  );

  return useSelector(selector);
}

// What the toolbar changes in the store, as Prowlarr's IndexerIndex dispatches them.
function useIndexDispatch() {
  const dispatch = useDispatch();

  return useMemo(
    () => ({
      onRefresh: () => dispatch(fetchMembers()),
      onTableOptionChange: (payload: unknown) =>
        dispatch(setMemberTableOption(payload)),
      onSortSelect: (sortKey: string) => dispatch(setMemberSort({ sortKey })),
      onFilterSelect: (selectedFilterKey: string | number) =>
        dispatch(setMemberFilter({ selectedFilterKey })),
    }),
    [dispatch]
  );
}

interface MembersBodyProps {
  state: MembersState;
  isSelectMode: boolean;
  isBusy: boolean;
  onAction: (members: PortalMember[], action: string) => void;
  onSortSelect: (sortKey: string) => void;
  onTableOptionChange: (payload: unknown) => void;
}

function MembersBody(props: MembersBodyProps) {
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
          state.totalItems ? 'PortalNoMembersMatch' : 'PortalNoMembers'
        )}
      </Alert>
    );
  }

  return (
    <>
      <MembersTable
        members={pageItems}
        columns={state.columns}
        sortKey={state.sortKey}
        sortDirection={state.sortDirection}
        isSelectMode={props.isSelectMode}
        isBusy={props.isBusy}
        onAction={props.onAction}
        onSortPress={props.onSortSelect}
        onTableOptionChange={props.onTableOptionChange}
      />
      <TablePager {...pager} />
      <MembersFooter members={state.items} />
    </>
  );
}

// Every member Requestarr knows: their access to each type with its limit, and how many requests they have, filtered,
// sorted and paged as Prowlarr's indexer list is, with Servarr's select mode for acting on several at once. Each row
// has its own actions too; Add member lists the server's people to onboard.
function MembersIndex() {
  const state = useMembers();
  const handlers = useIndexDispatch();
  const { onRefresh } = handlers;
  const history = useHistory();
  const [, selectDispatch] = useSelect();
  const [isSelectMode, setIsSelectMode] = useState(false);
  const onBarDone = useCallback(() => {
    selectDispatch({ type: 'unselectAll' });
    onRefresh();
  }, [selectDispatch, onRefresh]);
  const { run, ask, isBusy, outcome, dialogs } = useMemberActions(
    onRefresh,
    onBarDone
  );
  const onAddPress = useCallback(
    () => history.push(getPathWithUrlBase('/members/add')),
    [history]
  );
  const onSelectModePress = useCallback(
    () => setIsSelectMode((current) => !current),
    []
  );

  useEffect(() => {
    onRefresh();
  }, [onRefresh]);

  return (
    <PageContent title={translate('PortalMembers')}>
      <MembersToolbar
        {...state}
        {...handlers}
        isSelectMode={isSelectMode}
        isEmpty={!state.totalItems}
        onAddPress={onAddPress}
        onSelectModePress={onSelectModePress}
      />

      <PageContentBody>
        {state.isFetching && !state.isPopulated && <LoadingIndicator />}

        {!state.isFetching && !!state.error && (
          <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
        )}

        <ActionOutcomeAlert outcome={outcome} />

        {state.isPopulated && !state.error && (
          <MembersBody
            {...handlers}
            state={state}
            isSelectMode={isSelectMode}
            isBusy={isBusy}
            onAction={run}
          />
        )}
      </PageContentBody>

      {isSelectMode && (
        <MembersSelectFooter
          members={state.items}
          isBusy={isBusy}
          onAsk={ask}
        />
      )}
      {dialogs}
    </PageContent>
  );
}

// The page's select state, for its rows, the toolbar and the bar along the bottom.
function MembersPage() {
  const state = useMembers();

  return (
    <SelectProvider items={state.items}>
      <MembersIndex />
    </SelectProvider>
  );
}

export default MembersPage;
