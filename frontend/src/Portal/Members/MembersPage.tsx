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
import { toolbarActions, usePicked } from '../Components/Select/selectActions';
import { PortalMember } from '../PortalTypes';
import { GROUP_MEMBER_ACTIONS, memberActions } from './memberActionRules';
import MembersFooter from './MembersFooter';
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
        onSortPress={props.onSortSelect}
        onTableOptionChange={props.onTableOptionChange}
      />
      <TablePager {...pager} />
      <MembersFooter members={state.items} />
    </>
  );
}

type MemberAction = (members: PortalMember[], action: string) => void;

// The toolbar's actions on the members picked. One pick acts as their own page does (run); several ask first,
// naming everyone (ask); Onboard opens Add member with them all picked.
function useMemberSelectActions(
  members: PortalMember[],
  run: MemberAction,
  ask: MemberAction
) {
  const picked = usePicked(members);
  const onAction = useCallback(
    (name: string) =>
      name === 'onboard' || picked.length === 1
        ? run(picked, name)
        : ask(picked, name),
    [picked, run, ask]
  );

  return useMemo(
    () => toolbarActions(GROUP_MEMBER_ACTIONS, picked, memberActions, onAction),
    [picked, onAction]
  );
}

// Every member Requestarr knows: their access to each type with its limit, and how many requests they have, filtered,
// sorted and paged as Prowlarr's indexer list is. Select mode picks one or several; what can be done to them is in
// the toolbar, left of Add member, which lists the server's people to onboard.
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
  const selectActions = useMemberSelectActions(state.items, run, ask);
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
        selectActions={selectActions}
        isBusy={isBusy}
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
          />
        )}
      </PageContentBody>

      {dialogs}
    </PageContent>
  );
}

// The page's select state, for its rows and its toolbar.
function MembersPage() {
  const state = useMembers();

  return (
    <SelectProvider items={state.items}>
      <MembersIndex />
    </SelectProvider>
  );
}

export default MembersPage;
