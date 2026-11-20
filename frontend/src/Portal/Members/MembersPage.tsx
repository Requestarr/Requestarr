import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useHistory } from 'react-router-dom';
import { SelectProvider, useSelect } from 'App/SelectContext';
import ClientSideCollectionAppState from 'App/State/ClientSideCollectionAppState';
import { MemberIndexAppState, MembersAppState } from 'App/State/PortalAppState';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageJumpBar from 'Components/Page/PageJumpBar';
import withScrollPosition from 'Components/withScrollPosition';
import { kinds } from 'Helpers/Props';
import { fetchMembers } from 'Store/Actions/memberActions';
import {
  setMemberFilter,
  setMemberSort,
  setMemberTableOption,
} from 'Store/Actions/memberIndexActions';
import scrollPositions from 'Store/scrollPositions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import { jumpBarItems } from '../Components/Index/jumpBar';
import { toolbarActions, usePicked } from '../Components/Select/selectActions';
import { PortalMember } from '../PortalTypes';
import OnboardModal from './AddMembers/OnboardModal';
import { Who } from './AddMembers/useStartRun';
import { GROUP_MEMBER_ACTIONS, memberActions } from './memberActionRules';
import MembersFooter from './MembersFooter';
import MembersTable from './MembersTable';
import MembersToolbar from './MembersToolbar';
import useMemberActions from './useMemberActions';
import styles from '../Requests/RequestsPage.css';

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
  scrollerRef: React.RefObject<HTMLDivElement>;
  jumpToCharacter?: string;
  isSelectMode: boolean;
  isSmallScreen: boolean;
  onSortSelect: (sortKey: string) => void;
}

function MembersBody(props: MembersBodyProps) {
  const { state } = props;

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
    <div className={styles.contentBodyContainer}>
      <MembersTable
        members={state.items}
        columns={state.columns}
        sortKey={state.sortKey}
        sortDirection={state.sortDirection}
        jumpToCharacter={props.jumpToCharacter}
        scrollerRef={props.scrollerRef}
        isSelectMode={props.isSelectMode}
        isSmallScreen={props.isSmallScreen}
        onSortPress={props.onSortSelect}
      />
      <MembersFooter members={state.items} />
    </div>
  );
}

// The jump bar when sorted by name, the letter it last jumped to, and remembering where the page was scrolled, as
// Prowlarr's IndexerIndex.
function useJumpBar(state: MembersState) {
  const [jumpToCharacter, setJumpToCharacter] = useState<string>();
  const onScroll = useCallback(({ scrollTop }: { scrollTop: number }) => {
    setJumpToCharacter(undefined);
    scrollPositions.memberIndex = scrollTop;
  }, []);
  const items = useMemo(
    () =>
      jumpBarItems(
        state.items.map((member) => member.name ?? ''),
        state.sortKey === 'name',
        state.sortDirection
      ),
    [state.items, state.sortKey, state.sortDirection]
  );

  return {
    jumpToCharacter,
    onJumpBarItemPress: setJumpToCharacter,
    onScroll,
    items,
  };
}

type MemberAction = (members: PortalMember[], action: string) => void;

// Onboarding has its own toolbar button (Onboard), so it isn't one of the actions on the picks.
function actionsInToolbar(member: PortalMember) {
  return memberActions(member).filter((name) => name !== 'onboard');
}

// The toolbar's actions on the members picked, each for the picks that allow it: one acts as their own page does
// (run); several ask first, naming everyone (ask).
function useMemberSelectActions(
  members: PortalMember[],
  run: MemberAction,
  ask: MemberAction
) {
  const picked = usePicked(members);
  const onAction = useCallback(
    (name: string, able: PortalMember[]) =>
      able.length === 1 ? run(able, name) : ask(able, name),
    [run, ask]
  );
  const actions = useMemo(
    () =>
      toolbarActions(GROUP_MEMBER_ACTIONS, picked, actionsInToolbar, onAction),
    [picked, onAction]
  );

  return { picked, actions };
}

// Onboard: the picks it can onboard set up in one window; with none of those picked, the page that lists the server's
// people to onboard.
function useOnboard(picked: PortalMember[], onDone: () => void) {
  const history = useHistory();
  const [onboarding, setOnboarding] = useState<Who[]>([]);
  const onAddPress = useCallback(() => {
    const onboardable = picked.flatMap((member) =>
      member.discordId && memberActions(member).includes('onboard')
        ? [{ discordId: member.discordId, name: member.name }]
        : []
    );

    if (onboardable.length) {
      setOnboarding(onboardable);
    } else {
      history.push(getPathWithUrlBase('/members/add'));
    }
  }, [picked, history]);
  const onOnboardClose = useCallback(() => {
    setOnboarding([]);
    onDone();
  }, [onDone]);

  return { onboarding, onAddPress, onOnboardClose };
}

// Every member Requestarr knows: their access to each type with its limit, and how many requests they have, filtered,
// sorted in one scroll that draws only what's on screen, as Prowlarr's Indexer/Index/IndexerIndex lists the
// indexers. Select mode picks one or several; what can be done to them is in the toolbar, left of Onboard. Onboard sets up the picks it can onboard in one window; with none picked it lists the
// server's people to onboard.
function MembersIndex({ initialScrollTop }: { initialScrollTop?: number }) {
  const state = useMembers();
  const scrollerRef = useRef<HTMLDivElement>(null);
  const jumpBar = useJumpBar(state);
  const { isSmallScreen } = useSelector(createDimensionsSelector());
  const handlers = useIndexDispatch();
  const { onRefresh } = handlers;
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
  const select = useMemberSelectActions(state.items, run, ask);
  const onboard = useOnboard(select.picked, onBarDone);
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
        selectActions={select.actions}
        isBusy={isBusy}
        isSelectMode={isSelectMode}
        isEmpty={!state.totalItems}
        onAddPress={onboard.onAddPress}
        onSelectModePress={onSelectModePress}
      />

      <div className={styles.pageContentBodyWrapper}>
        <PageContentBody
          ref={scrollerRef}
          className={styles.contentBody}
          innerClassName={styles.tableInnerContentBody}
          initialScrollTop={initialScrollTop}
          onScroll={jumpBar.onScroll}
        >
          {state.isFetching && !state.isPopulated && <LoadingIndicator />}

          {!state.isFetching && !!state.error && (
            <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
          )}

          <ActionOutcomeAlert outcome={outcome} />

          {state.isPopulated && !state.error && (
            <MembersBody
              state={state}
              scrollerRef={scrollerRef}
              jumpToCharacter={jumpBar.jumpToCharacter}
              isSelectMode={isSelectMode}
              isSmallScreen={isSmallScreen}
              onSortSelect={handlers.onSortSelect}
            />
          )}
        </PageContentBody>

        {state.isPopulated && !!jumpBar.items.order.length && (
          <PageJumpBar
            items={jumpBar.items}
            onItemPress={jumpBar.onJumpBarItemPress}
          />
        )}
      </div>

      {dialogs}
      <OnboardModal
        people={onboard.onboarding}
        onClose={onboard.onOnboardClose}
      />
    </PageContent>
  );
}

// The page's select state, for its rows and its toolbar, and where it was scrolled when you come back to it.
const MembersPage = withScrollPosition(
  ({ initialScrollTop }: { initialScrollTop?: number }) => {
    const state = useMembers();

    return (
      <SelectProvider items={state.items}>
        <MembersIndex initialScrollTop={initialScrollTop} />
      </SelectProvider>
    );
  },
  'memberIndex'
);

export default MembersPage;
