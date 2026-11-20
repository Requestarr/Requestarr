import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import MemberLink from '../Components/MemberLink';
import { PagedSort } from '../Components/Paging/PagedSection';
import { PortalFollower } from '../PortalTypes';

const columns: Column[] = [
  { name: 'member', label: () => translate('PortalMember') },
  { name: 'followed', label: () => translate('PortalFollowed') },
].map((column) => ({ ...column, isVisible: true, isSortable: true }));

interface FollowersTableProps {
  followers: PortalFollower[];
  sort: PagedSort;
}

// A page of the people following a request, with their picture, name and @handle and when they followed, in the
// order the server sorted them (earliest first until a column is pressed).
function FollowersTable({ followers, sort }: FollowersTableProps) {
  const { sortKey, sortDirection, onSortPress } = sort;

  return (
    <Table
      columns={columns}
      sortKey={sortKey}
      sortDirection={sortDirection}
      onSortPress={onSortPress}
    >
      <TableBody>
        {followers.map((follower) => (
          <TableRow key={follower.person.id}>
            <TableRowCell>
              <MemberLink person={follower.person} />
            </TableRowCell>
            <RelativeDateCell date={follower.followed} />
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

export default FollowersTable;
