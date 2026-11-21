import React, {
  useCallback,
  useEffect,
  useMemo,
  useRef,
  useState,
} from 'react';
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
import PageJumpBar from 'Components/Page/PageJumpBar';
import withScrollPosition from 'Components/withScrollPosition';
import { kinds } from 'Helpers/Props';
import { fetchRequests } from 'Store/Actions/requestActions';
import {
  setRequestFilter,
  setRequestSort,
  setRequestTableOption,
  setRequestView,
} from 'Store/Actions/requestIndexActions';
import scrollPositions from 'Store/scrollPositions';
import createClientSideCollectionSelector from 'Store/Selectors/createClientSideCollectionSelector';
import createDimensionsSelector from 'Store/Selectors/createDimensionsSelector';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import { jumpBarItems } from '../Components/Index/jumpBar';
import RequestIndexOverviews from './Overview/RequestIndexOverviews';
import RequestIndexPosters from './Posters/RequestIndexPosters';
import RequestIndexOptionsModal from './RequestIndexOptionsModal';
import { RequestIndexViewProps } from './RequestIndexViewProps';
import RequestsFooter from './RequestsFooter';
import RequestsToolbar from './RequestsToolbar';
import RequestIndexTable from './Table/RequestIndexTable';
import useRequestActionRunner from './useRequestActionRunner';
import useRequestBulk from './useRequestBulk';
import styles from './RequestsPage.css';

// The page body's inner class for each view, as Sonarr's SeriesIndex picks it: posters have less padding.
const INNER_CLASS: Record<string, string> = {
  posters: styles.postersInnerContentBody,
  overview: styles.overviewInnerContentBody,
  table: styles.tableInnerContentBody,
};

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

// The view chosen, as Sonarr main's SeriesIndex picks its view component.
function RequestIndexView(props: RequestIndexViewProps & { view: string }) {
  const { view, ...viewProps } = props;

  switch (view) {
    case 'posters':
      return <RequestIndexPosters {...viewProps} />;
    case 'overview':
      return <RequestIndexOverviews {...viewProps} />;
    default:
      return <RequestIndexTable {...viewProps} />;
  }
}

// What the toolbar changes in the store, as Sonarr main's SeriesIndex dispatches them. A new view starts at the top.
function useIndexDispatch(scrollerRef: React.RefObject<HTMLDivElement>) {
  const dispatch = useDispatch();

  return useMemo(
    () => ({
      onRefreshPress: () => dispatch(fetchRequests()),
      onTableOptionChange: (payload: unknown) =>
        dispatch(setRequestTableOption(payload)),
      onViewSelect: (view: string) => {
        dispatch(setRequestView({ view }));
        scrollerRef.current?.scrollTo(0, 0);
      },
      onSortSelect: (sortKey: string) => dispatch(setRequestSort({ sortKey })),
      onFilterSelect: (selectedFilterKey: string | number) =>
        dispatch(setRequestFilter({ selectedFilterKey })),
    }),
    [dispatch, scrollerRef]
  );
}

// The jump bar, the letter it last jumped to, and remembering where the page was scrolled, as Sonarr's SeriesIndex.
function useJumpBar(state: RequestsState) {
  const [jumpToCharacter, setJumpToCharacter] = useState<string>();
  const onScroll = useCallback(({ scrollTop }: { scrollTop: number }) => {
    setJumpToCharacter(undefined);
    scrollPositions.requestIndex = scrollTop;
  }, []);
  const items = useMemo(
    () =>
      jumpBarItems(
        state.items.map((request) => request.title),
        state.sortKey === 'title',
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

// Whether there are requests to show: loaded without an error, and at least one left after the filter.
function hasRequestsToShow(state: RequestsState) {
  return !state.error && state.isPopulated && !!state.items.length;
}

interface RequestsBodyProps {
  state: RequestsState;
  runnerOutcome: ReturnType<typeof useRequestActionRunner>['outcome'];
  scrollerRef: React.RefObject<HTMLDivElement>;
  jumpToCharacter?: string;
  isSelectMode: boolean;
  isSmallScreen: boolean;
}

// What the page body shows: loading, an error, the view chosen with its footer, or that there's nothing to show.
function RequestsBody(props: RequestsBodyProps) {
  const { state } = props;

  return (
    <>
      {state.isFetching && !state.isPopulated && <LoadingIndicator />}

      {!state.isFetching && !!state.error && (
        <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
      )}

      <ActionOutcomeAlert outcome={props.runnerOutcome} />

      {hasRequestsToShow(state) && (
        <div className={styles.contentBodyContainer}>
          <RequestIndexView
            view={state.view}
            scrollerRef={props.scrollerRef}
            items={state.items}
            sortKey={state.sortKey}
            sortDirection={state.sortDirection}
            jumpToCharacter={props.jumpToCharacter}
            isSelectMode={props.isSelectMode}
            isSmallScreen={props.isSmallScreen}
          />

          <RequestsFooter requests={state.items} />
        </div>
      )}

      {!state.error && state.isPopulated && !state.items.length && (
        <Alert kind={kinds.INFO}>
          {translate(
            state.totalItems ? 'PortalNoRequestsMatch' : 'PortalNoRequests'
          )}
        </Alert>
      )}
    </>
  );
}

interface RequestsIndexProps {
  initialScrollTop?: number;
}

// Every request, in the Table, Posters or Overview view, filtered and sorted in one scroll that draws only what's on
// screen, as Sonarr main's Series/Index/SeriesIndex lists the series. Select mode picks one or several; what can be
// done to them is in the toolbar, left of Refresh.
function RequestsIndex({ initialScrollTop }: RequestsIndexProps) {
  const state = useRequests();
  const scrollerRef = useRef<HTMLDivElement>(null);
  const handlers = useIndexDispatch(scrollerRef);
  const { onRefreshPress } = handlers;
  const { isSmallScreen } = useSelector(createDimensionsSelector());
  const [isSelectMode, setIsSelectMode] = useState(false);
  const [isOptionsModalOpen, setIsOptionsModalOpen] = useState(false);
  const jumpBar = useJumpBar(state);
  const runner = useRequestActionRunner(onRefreshPress);
  const bulk = useRequestBulk(state.items, runner.run, onRefreshPress);
  const onSelectModePress = useCallback(
    () => setIsSelectMode((current) => !current),
    []
  );
  const onOptionsPress = useCallback(() => setIsOptionsModalOpen(true), []);
  const onOptionsModalClose = useCallback(
    () => setIsOptionsModalOpen(false),
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
        onOptionsPress={onOptionsPress}
      />

      <div className={styles.pageContentBodyWrapper}>
        <PageContentBody
          ref={scrollerRef}
          className={styles.contentBody}
          innerClassName={
            INNER_CLASS[state.view] ?? styles.tableInnerContentBody
          }
          initialScrollTop={initialScrollTop}
          onScroll={jumpBar.onScroll}
        >
          <RequestsBody
            state={state}
            runnerOutcome={runner.outcome}
            scrollerRef={scrollerRef}
            jumpToCharacter={jumpBar.jumpToCharacter}
            isSelectMode={isSelectMode}
            isSmallScreen={isSmallScreen}
          />
        </PageContentBody>

        {hasRequestsToShow(state) && !!jumpBar.items.order.length && (
          <PageJumpBar
            items={jumpBar.items}
            onItemPress={jumpBar.onJumpBarItemPress}
          />
        )}
      </div>

      <RequestIndexOptionsModal
        view={state.view}
        isOpen={isOptionsModalOpen}
        onModalClose={onOptionsModalClose}
      />
      {runner.dialogs}
      {bulk.modal}
    </PageContent>
  );
}

// The page's select state, for its rows and its toolbar, and where it was scrolled when you come back to it.
const RequestsPage = withScrollPosition(
  ({ initialScrollTop }: RequestsIndexProps) => {
    const state = useRequests();

    return (
      <SelectProvider items={state.items}>
        <RequestsIndex initialScrollTop={initialScrollTop} />
      </SelectProvider>
    );
  },
  'requestIndex'
);

export default RequestsPage;
