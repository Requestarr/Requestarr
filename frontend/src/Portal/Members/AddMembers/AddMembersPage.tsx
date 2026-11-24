import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { SelectProvider, useSelect } from 'App/SelectContext';
import AppState from 'App/State/AppState';
import ClientSideCollectionAppState from 'App/State/ClientSideCollectionAppState';
import { ServerMembersAppState } from 'App/State/PortalAppState';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import {
  fetchServerMembers,
  setServerMemberFilter,
  setServerMemberSort,
  setServerMemberTableOption,
} from 'Store/Actions/serverMemberActions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import getErrorMessage from 'Utilities/Object/getErrorMessage';
import translate from 'Utilities/String/translate';
import { usePicked } from '../../Components/Select/selectActions';
import AddMembersTable, { Candidate } from './AddMembersTable';
import AddMembersToolbar from './AddMembersToolbar';
import OnboardModal from './OnboardModal';
import { Who } from './useStartRun';

type ServerMembersState = ServerMembersAppState & ClientSideCollectionAppState;

function selectAll(state: AppState) {
  return state.serverMembers.items;
}

// The people as rows: everyone, and those the filter shows in the page's sort.
function useCandidates() {
  const selector = useMemo(
    () => createClientSideCollectionSelector('serverMembers'),
    []
  );
  const state: ServerMembersState = useSelector(selector);
  const people = useSelector(selectAll);
  const everyone = useMemo(
    () => people.map((person, index) => ({ ...person, id: index })),
    [people]
  );
  const shown = useMemo(() => {
    const byDiscordId = new Map(everyone.map((c) => [c.discordId, c]));

    return state.items.flatMap((p) => byDiscordId.get(p.discordId) ?? []);
  }, [state.items, everyone]);

  return { state, everyone, shown };
}

// What the toolbar and the table change in the store.
function useListDispatch() {
  const dispatch = useDispatch();

  return useMemo(
    () => ({
      onRefreshPress: () => dispatch(fetchServerMembers()),
      onTableOptionChange: (payload: unknown) =>
        dispatch(setServerMemberTableOption(payload)),
      onSortPress: (sortKey: string) =>
        dispatch(setServerMemberSort({ sortKey })),
      onFilterSelect: (selectedFilterKey: string | number) =>
        dispatch(setServerMemberFilter({ selectedFilterKey })),
    }),
    [dispatch]
  );
}

interface ListProps {
  state: ServerMembersState;
  shown: Candidate[];
  onSortPress: (sortKey: string) => void;
}

function List({ state, shown, onSortPress }: ListProps) {
  if (state.error && !state.isFetching) {
    return (
      <Alert kind={kinds.DANGER}>
        {getErrorMessage(state.error, translate('PortalLoadError'))}
      </Alert>
    );
  }

  if (!state.isPopulated) {
    return <LoadingIndicator />;
  }

  return state.totalItems ? (
    <AddMembersTable
      candidates={shown}
      columns={state.columns}
      sortKey={state.sortKey}
      sortDirection={state.sortDirection}
      onSortPress={onSortPress}
    />
  ) : (
    <Alert kind={kinds.INFO}>{translate('PortalNobodyToAdd')}</Alert>
  );
}

interface OnboardListProps extends Omit<ListProps, 'onSortPress'> {
  everyone: Candidate[];
}

function OnboardList({ state, everyone, shown }: OnboardListProps) {
  const { onSortPress, ...handlers } = useListDispatch();
  const { onRefreshPress } = handlers;
  const [, selectDispatch] = useSelect();
  const picked = usePicked(everyone);
  const [onboarding, setOnboarding] = useState<Who[]>([]);
  const onOnboardPress = useCallback(() => setOnboarding(picked), [picked]);
  const onOnboardClose = useCallback(() => {
    setOnboarding([]);
    selectDispatch({ type: 'unselectAll' });
    onRefreshPress();
  }, [selectDispatch, onRefreshPress]);

  // Opening the page reads the list again, from before it's read nothing is shown.
  useEffect(() => {
    onRefreshPress();
  }, [onRefreshPress]);

  return (
    <PageContent title={translate('PortalActionOnboard')}>
      <AddMembersToolbar
        {...state}
        {...handlers}
        isEmpty={!state.totalItems}
        hasPicks={picked.length > 0}
        onOnboardPress={onOnboardPress}
      />

      <PageContentBody>
        <List state={state} shown={shown} onSortPress={onSortPress} />
      </PageContentBody>

      <OnboardModal people={onboarding} onClose={onOnboardClose} />
    </PageContent>
  );
}

// Onboard: everyone in the Discord server who isn't onboarded, to pick one or many and set up together in one window,
// paced for Discord, with each person's progress.
function AddMembersPage() {
  const candidates = useCandidates();

  return (
    <SelectProvider items={candidates.everyone}>
      <OnboardList {...candidates} />
    </SelectProvider>
  );
}

export default AddMembersPage;
