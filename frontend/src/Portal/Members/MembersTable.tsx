import React, { ReactNode, useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import SortDirection from 'Helpers/Props/SortDirection';
import { accessFor } from 'Store/Actions/memberActions';
import Person from '../Components/Person';
import SelectCell from '../Components/Select/SelectCell';
import { byTypeText } from '../portalText';
import { ContentType, PortalMember } from '../PortalTypes';
import MemberStatusLabel from './MemberStatusLabel';

const ACCESS_PREFIX = 'access-';
const COUNTS = ['requests', 'active', 'pending', 'following'] as const;
const DATES = ['firstRequest', 'lastRequest', 'added'] as const;

// One cell of a member's row, by its column's name, as Sonarr main's SeriesIndexRow draws them.
function memberCell(name: string, member: PortalMember): ReactNode {
  if (name.startsWith(ACCESS_PREFIX)) {
    const type = name.slice(ACCESS_PREFIX.length) as ContentType;

    return <TableRowCell key={name}>{accessFor(member, type)}</TableRowCell>;
  }

  if ((COUNTS as readonly string[]).includes(name)) {
    return (
      <TableRowCell key={name}>
        {member[name as (typeof COUNTS)[number]]}
      </TableRowCell>
    );
  }

  if ((DATES as readonly string[]).includes(name)) {
    return (
      <RelativeDateCell
        key={name}
        date={member[name as (typeof DATES)[number]] ?? ''}
      />
    );
  }

  switch (name) {
    case 'name':
      return (
        <TableRowCell key={name}>
          <Link to={`/members/${member.id}`}>
            <Person
              name={member.name}
              handle={member.discordName}
              avatar={member.avatar}
            />
          </Link>
        </TableRowCell>
      );
    case 'status':
      return (
        <TableRowCell key={name}>
          <MemberStatusLabel status={member.status} />
        </TableRowCell>
      );
    case 'byType':
      return <TableRowCell key={name}>{byTypeText(member)}</TableRowCell>;
    default:
      // A column this version no longer has, still remembered in the browser.
      return null;
  }
}

interface MembersTableProps {
  members: PortalMember[];
  columns: Column[];
  sortKey: string;
  sortDirection: SortDirection;
  isSelectMode: boolean;
  onSortPress: (sortKey: string) => void;
  onTableOptionChange: (payload: unknown) => void;
}

// The Members page's table: its columns from Table Options, and in select mode a box on each row and one in the
// header that picks every member the filter shows, as Prowlarr's IndexerIndexTable has.
function MembersTable(props: MembersTableProps) {
  const { members, columns, isSelectMode } = props;
  const [selectState, selectDispatch] = useSelect();
  const onSelectAllChange = useCallback(
    ({ value }: { value: boolean }) =>
      selectDispatch({ type: value ? 'selectAll' : 'unselectAll' }),
    [selectDispatch]
  );
  const visible = columns.filter((column) => column.isVisible);

  return (
    <Table
      selectAll={isSelectMode}
      allSelected={selectState.allSelected}
      allUnselected={selectState.allUnselected}
      columns={columns}
      sortKey={props.sortKey}
      sortDirection={props.sortDirection}
      onSelectAllChange={onSelectAllChange}
      onSortPress={props.onSortPress}
      onTableOptionChange={props.onTableOptionChange}
    >
      <TableBody>
        {members.map((member) => (
          <TableRow key={member.id}>
            {isSelectMode && <SelectCell id={member.id} />}
            {visible.map((column) => memberCell(column.name, member))}
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default MembersTable;
