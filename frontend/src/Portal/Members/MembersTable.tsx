import React, { ReactNode, useCallback, useMemo } from 'react';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import VirtualTableRowCell from 'Components/Table/Cells/VirtualTableRowCell';
import Column from 'Components/Table/Column';
import { accessFor } from 'Store/Actions/memberActions';
import IndexTable from '../Components/Index/IndexTable';
import Person from '../Components/Person';
import { VirtualSelectCell } from '../Components/Select/SelectCell';
import { byTypeText } from '../portalText';
import { ContentType, PortalMember } from '../PortalTypes';
import MemberStatusLabel from './MemberStatusLabel';
import headerStyles from './MembersTableHeader.css';
import styles from './MembersTableRow.css';

const ACCESS_PREFIX = 'access-';
const COUNTS = ['requests', 'active', 'pending', 'following'] as const;
const DATES = ['firstRequest', 'lastRequest', 'added'] as const;

// A row's height: a member's picture with their name and @handle on two lines, and the cell's padding.
const ROW_HEIGHT = 54;

// The class giving a column its width, by the column's name: every access column takes the access width.
function classOf(classes: object, name: string) {
  const styleName = name.startsWith(ACCESS_PREFIX) ? 'access' : name;

  return (classes as Record<string, string>)[styleName];
}

function cellClass(name: string) {
  return classOf(styles, name) ?? styles.cell;
}

// What a column shows for a member, apart from its dates.
function contentOf(name: string, member: PortalMember): ReactNode {
  if (name.startsWith(ACCESS_PREFIX)) {
    return accessFor(member, name.slice(ACCESS_PREFIX.length) as ContentType);
  }

  if ((COUNTS as readonly string[]).includes(name)) {
    return member[name as (typeof COUNTS)[number]];
  }

  switch (name) {
    case 'name':
      return (
        <Link to={`/members/${member.id}`}>
          <Person
            name={member.name}
            handle={member.discordName}
            avatar={member.avatar}
          />
        </Link>
      );
    case 'status':
      return <MemberStatusLabel status={member.status} />;
    case 'byType':
      return byTypeText(member);
    default:
      return null;
  }
}

// One cell of a member's row, by its column's name, as Prowlarr's IndexerIndexRow draws them.
function memberCell(name: string, member: PortalMember): ReactNode {
  if ((DATES as readonly string[]).includes(name)) {
    return (
      <RelativeDateCell
        key={name}
        className={cellClass(name)}
        date={member[name as (typeof DATES)[number]] ?? ''}
        component={VirtualTableRowCell}
      />
    );
  }

  return (
    <VirtualTableRowCell key={name} className={cellClass(name)}>
      {contentOf(name, member)}
    </VirtualTableRowCell>
  );
}

interface MembersTableProps {
  members: PortalMember[];
  columns: Column[];
  sortKey: string;
  sortDirection: string;
  jumpToCharacter?: string;
  scrollerRef: React.RefObject<HTMLElement>;
  isSelectMode: boolean;
  isSmallScreen: boolean;
  onSortPress: (sortKey: string) => void;
}

// The Members page's table, drawing only the rows on screen: its columns from Table Options, and in select mode a box
// on each row and one in the header that picks every member the filter shows, as Prowlarr's IndexerIndexTable has.
function MembersTable(props: MembersTableProps) {
  const { members, columns, isSelectMode } = props;
  const texts = useMemo(
    () => members.map((member) => member.name ?? ''),
    [members]
  );
  const renderRow = useCallback(
    (member: PortalMember) => (
      <>
        {isSelectMode ? <VirtualSelectCell id={member.id} /> : null}
        {columns
          .filter((column) => column.isVisible)
          .map((column) => memberCell(column.name, member))}
      </>
    ),
    [columns, isSelectMode]
  );

  const columnStyles = useMemo(
    () =>
      Object.fromEntries(
        columns.map(({ name }) => [name, classOf(headerStyles, name)])
      ),
    [columns]
  );

  return (
    <IndexTable
      {...props}
      items={members}
      columnStyles={columnStyles}
      texts={texts}
      rowHeight={ROW_HEIGHT}
      renderRow={renderRow}
    />
  );
}

export default MembersTable;
