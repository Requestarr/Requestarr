import React, { ReactNode, RefObject, useCallback, useRef } from 'react';
import { FixedSizeList as List, ListChildComponentProps } from 'react-window';
import Scroller from 'Components/Scroller/Scroller';
import Column from 'Components/Table/Column';
import ScrollDirection from 'Helpers/Props/ScrollDirection';
import IndexTableHeader from './IndexTableHeader';
import useIndexScroll from './useIndexScroll';
import useJumpToCharacter from './useJumpToCharacter';
import styles from './IndexTable.css';

// The approximate height of the header and padding, added when jumping below the first row, as Sonarr's table.
const JUMP_OFFSET = 57;

interface RowItemData<T> {
  items: T[];
  renderRow: (item: T) => ReactNode;
}

function Row<T>({
  index,
  style,
  data,
}: ListChildComponentProps<RowItemData<T>>) {
  if (index >= data.items.length) {
    return null;
  }

  return (
    <div
      style={{ display: 'flex', justifyContent: 'space-between', ...style }}
      className={styles.row}
    >
      {data.renderRow(data.items[index])}
    </div>
  );
}

interface IndexTableProps<T> {
  items: T[];
  columns: Column[];

  // Each column's width, by its name, for the header (its cells take the same).
  columnStyles: Record<string, string>;

  // Each item's title or name, for the jump bar.
  texts: string[];
  rowHeight: number;
  sortKey?: string;
  sortDirection?: string;
  jumpToCharacter?: string;
  scrollerRef: RefObject<HTMLElement>;
  isSelectMode: boolean;
  isSmallScreen: boolean;
  onSortPress: (sortKey: string) => void;

  // The row's cells: its box in select mode, then a cell for each column shown.
  renderRow: (item: T) => ReactNode;
}

// A list in a table that draws only the rows on screen, as Sonarr main's Series/Index/Table/SeriesIndexTable lists the
// series and Prowlarr's IndexerIndexTable the indexers: the header, then a row for each item.
function IndexTable<T>(props: IndexTableProps<T>) {
  const { items, rowHeight, scrollerRef, isSmallScreen } = props;
  const listRef = useRef<List<RowItemData<T>>>(null);
  const onScrollTop = useCallback(
    (scrollTop: number) => listRef.current?.scrollTo(scrollTop),
    []
  );
  const { measureRef, size } = useIndexScroll(
    scrollerRef,
    isSmallScreen,
    onScrollTop
  );
  const scrollTopOf = useCallback(
    (index: number) => (index > 0 ? index * rowHeight + JUMP_OFFSET : 0),
    [rowHeight]
  );

  useJumpToCharacter({
    texts: props.texts,
    jumpToCharacter: props.jumpToCharacter,
    scrollerRef,
    scrollTopOf,
    onScrollTop,
  });

  return (
    <div ref={measureRef}>
      <Scroller
        className={styles.tableScroller}
        scrollDirection={ScrollDirection.Horizontal}
      >
        <IndexTableHeader
          columns={props.columns}
          columnStyles={props.columnStyles}
          sortKey={props.sortKey}
          sortDirection={props.sortDirection}
          isSelectMode={props.isSelectMode}
          onSortPress={props.onSortPress}
        />
        <List<RowItemData<T>>
          ref={listRef}
          style={{ width: '100%', height: '100%', overflow: 'none' }}
          width={size.width}
          height={size.height}
          itemCount={items.length}
          itemSize={rowHeight}
          itemData={{ items, renderRow: props.renderRow }}
        >
          {Row}
        </List>
      </Scroller>
    </div>
  );
}

export default IndexTable;
