import React, { useCallback, useMemo, useRef } from 'react';
import { useSelector } from 'react-redux';
import { FixedSizeList as List, ListChildComponentProps } from 'react-window';
import AppState from 'App/State/AppState';
import { RequestOverviewOptions } from 'App/State/PortalAppState';
import dimensions from 'Styles/Variables/dimensions';
import useIndexScroll from '../../Components/Index/useIndexScroll';
import useJumpToCharacter from '../../Components/Index/useJumpToCharacter';
import { PortalRequest } from '../../PortalTypes';
import { RequestIndexViewProps } from '../RequestIndexViewProps';
import RequestIndexOverview from './RequestIndexOverview';
import styles from './RequestIndexOverviews.css';

const columnPadding = parseInt(dimensions.requestIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(
  dimensions.requestIndexColumnPaddingSmallScreen
);
const progressBarHeight = parseInt(dimensions.progressBarSmallHeight);
const detailedProgressBarHeight = parseInt(dimensions.progressBarMediumHeight);

// Sonarr's posters are 170 wide and 250 high.
const POSTER_RATIO = 250 / 170;

// Each poster size as a share of the largest, as Sonarr's overview has them.
const POSTER_SCALE: Record<string, number> = {
  small: 0.5,
  medium: 0.75,
  large: 1,
};

// The approximate height of the header and padding, added when jumping below the first row, as Sonarr's overview.
const JUMP_OFFSET = 57;

interface RowItemData {
  items: PortalRequest[];
  options: RequestOverviewOptions;
  posterWidth: number;
  posterHeight: number;
  rowHeight: number;
  isSelectMode: boolean;
  isSmallScreen: boolean;
}

function Row({ index, style, data }: ListChildComponentProps<RowItemData>) {
  const { items, ...otherData } = data;

  if (index >= items.length) {
    return null;
  }

  return (
    <div className={styles.container} style={style}>
      <RequestIndexOverview request={items[index]} {...otherData} />
    </div>
  );
}

// Every request with its poster, overview and details, in a list that draws only the rows on screen, as Sonarr main's
// Series/Index/Overview/SeriesIndexOverviews lists the series.
function RequestIndexOverviews(props: RequestIndexViewProps) {
  const { items, jumpToCharacter, scrollerRef, isSelectMode, isSmallScreen } =
    props;
  const options = useSelector(
    (state: AppState) => state.requestIndex.overviewOptions
  );
  const listRef = useRef<List>(null);
  const onScrollTop = useCallback(
    (scrollTop: number) => listRef.current?.scrollTo(scrollTop),
    []
  );
  const { measureRef, size } = useIndexScroll(
    scrollerRef,
    isSmallScreen,
    onScrollTop
  );

  const posterWidth = Math.floor(
    (isSmallScreen ? 152 : 162) * POSTER_SCALE[options.size]
  );
  const posterHeight = Math.ceil(POSTER_RATIO * posterWidth);
  const rowHeight =
    posterHeight +
    (options.detailedProgressBar
      ? detailedProgressBarHeight
      : progressBarHeight) +
    (isSmallScreen ? columnPaddingSmallScreen : columnPadding);

  const texts = useMemo(() => items.map((request) => request.title), [items]);
  const scrollTopOf = useCallback(
    (index: number) => (index > 0 ? index * rowHeight + JUMP_OFFSET : 0),
    [rowHeight]
  );

  useJumpToCharacter({
    texts,
    jumpToCharacter,
    scrollerRef,
    scrollTopOf,
    onScrollTop,
  });

  return (
    <div ref={measureRef}>
      <List<RowItemData>
        ref={listRef}
        style={{ width: '100%', height: '100%', overflow: 'none' }}
        width={size.width}
        height={size.height}
        itemCount={items.length}
        itemSize={rowHeight}
        itemData={{
          items,
          options,
          posterWidth,
          posterHeight,
          rowHeight,
          isSelectMode,
          isSmallScreen,
        }}
      >
        {Row}
      </List>
    </div>
  );
}

export default RequestIndexOverviews;
