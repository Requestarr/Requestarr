import React, { useCallback, useMemo, useRef } from 'react';
import { useSelector } from 'react-redux';
import { FixedSizeGrid as Grid, GridChildComponentProps } from 'react-window';
import AppState from 'App/State/AppState';
import { RequestPosterOptions } from 'App/State/PortalAppState';
import dimensions from 'Styles/Variables/dimensions';
import useIndexScroll from '../../Components/Index/useIndexScroll';
import useJumpToCharacter from '../../Components/Index/useJumpToCharacter';
import { PortalRequest } from '../../PortalTypes';
import { RequestIndexViewProps } from '../RequestIndexViewProps';
import RequestIndexPoster from './RequestIndexPoster';

const columnPadding = parseInt(dimensions.requestIndexColumnPadding);
const columnPaddingSmallScreen = parseInt(
  dimensions.requestIndexColumnPaddingSmallScreen
);
const progressBarHeight = parseInt(dimensions.progressBarSmallHeight);
const detailedProgressBarHeight = parseInt(dimensions.progressBarMediumHeight);

// Each line under a poster (RequestIndexPoster.css .title and RequestIndexPosterInfo.css .info).
const LINE_HEIGHT = 19;

// Sorts whose value the poster only shows on the sort's own line (RequestIndexPosterInfo).
const SORTS_WITH_LINE = [
  'type',
  'status',
  'instance',
  'requested',
  'decided',
  'fulfilled',
];

// How many more columns than fit at full width each poster size takes, as Sonarr's posters.
const ADDITIONAL_COLUMN_COUNT: Record<string, number> = {
  small: 3,
  medium: 2,
  large: 1,
};

// Sonarr's posters are 170 wide and 250 high.
const POSTER_RATIO = 250 / 170;

interface CellItemData {
  layout: {
    columnCount: number;
    padding: number;
    posterWidth: number;
    posterHeight: number;
  };
  items: PortalRequest[];
  sortKey: string;
  isSelectMode: boolean;
}

function Cell({
  columnIndex,
  rowIndex,
  style,
  data,
}: GridChildComponentProps<CellItemData>) {
  const { layout, items, sortKey, isSelectMode } = data;
  const { columnCount, padding, posterWidth, posterHeight } = layout;
  const index = rowIndex * columnCount + columnIndex;

  if (index >= items.length) {
    return null;
  }

  return (
    <div style={{ padding, ...style }}>
      <RequestIndexPoster
        request={items[index]}
        sortKey={sortKey}
        isSelectMode={isSelectMode}
        posterWidth={posterWidth}
        posterHeight={posterHeight}
      />
    </div>
  );
}

// The width of a column for the page's width and the poster size chosen, as Sonarr's posters work it out.
function columnWidthFor(width: number, size: string, isSmallScreen: boolean) {
  const maximumColumnWidth = isSmallScreen ? 172 : 182;
  const columns = Math.floor(width / maximumColumnWidth);

  return width % maximumColumnWidth === 0
    ? maximumColumnWidth
    : Math.floor(width / (columns + ADDITIONAL_COLUMN_COUNT[size]));
}

// A row's height: the poster, its bar and the lines under it, as Sonarr's posters add them up.
function rowHeightFor(
  options: RequestPosterOptions,
  sortKey: string,
  posterHeight: number,
  padding: number
) {
  const { detailedProgressBar, showTitle, showRequester, showFollowers } =
    options;
  const lines =
    [showTitle, showRequester, showFollowers].filter(Boolean).length +
    (SORTS_WITH_LINE.includes(sortKey) ? 1 : 0);

  return (
    posterHeight +
    (detailedProgressBar ? detailedProgressBarHeight : progressBarHeight) +
    lines * LINE_HEIGHT +
    padding * 2
  );
}

// Every request as a poster in a grid that draws only the rows on screen, as Sonarr main's
// Series/Index/Posters/SeriesIndexPosters lays out the series.
function RequestIndexPosters(props: RequestIndexViewProps) {
  const {
    scrollerRef,
    items,
    sortKey,
    jumpToCharacter,
    isSelectMode,
    isSmallScreen,
  } = props;

  const posterOptions = useSelector(
    (state: AppState) => state.requestIndex.posterOptions
  );
  const ref = useRef<Grid>(null);
  const onScrollTop = useCallback(
    (scrollTop: number) => ref.current?.scrollTo({ scrollLeft: 0, scrollTop }),
    []
  );
  const { measureRef, size } = useIndexScroll(
    scrollerRef,
    isSmallScreen,
    onScrollTop
  );

  const columnWidth = columnWidthFor(
    size.width,
    posterOptions.size,
    isSmallScreen
  );
  const columnCount = Math.max(Math.floor(size.width / columnWidth), 1);
  const padding = isSmallScreen ? columnPaddingSmallScreen : columnPadding;
  const posterWidth = columnWidth - padding * 2;
  const posterHeight = Math.ceil(POSTER_RATIO * posterWidth);

  const rowHeight = rowHeightFor(posterOptions, sortKey, posterHeight, padding);
  const texts = useMemo(() => items.map((request) => request.title), [items]);
  const scrollTopOf = useCallback(
    (index: number) => Math.floor(index / columnCount) * rowHeight + padding,
    [columnCount, rowHeight, padding]
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
      <Grid<CellItemData>
        ref={ref}
        style={{ width: '100%', height: '100%', overflow: 'none' }}
        width={size.width}
        height={size.height}
        columnCount={columnCount}
        columnWidth={columnWidth}
        rowCount={Math.ceil(items.length / columnCount)}
        rowHeight={rowHeight}
        itemData={{
          layout: { columnCount, padding, posterWidth, posterHeight },
          items,
          sortKey,
          isSelectMode,
        }}
      >
        {Cell}
      </Grid>
    </div>
  );
}

export default RequestIndexPosters;
