import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import MemberLink from '../Components/MemberLink';
import { PortalFollower } from '../PortalTypes';
import useSortedRows, { SortValues } from '../useSortedRows';
import styles from './FollowersTable.css';

const columns: Column[] = [
  { name: 'member', label: () => translate('PortalMember') },
  { name: 'followed', label: () => translate('PortalFollowed') },
].map((column) => ({ ...column, isVisible: true, isSortable: true }));

const sortValues: SortValues<PortalFollower> = {
  member: (f) => f.person.name,
  followed: (f) => f.followed,
};

// Everyone following a request, with their picture, name and @handle and when they followed, earliest first until a
// column is pressed. A long list scrolls inside its own box, so the page below it stays in reach.
function FollowersTable({ followers }: { followers: PortalFollower[] }) {
  const { sorted, sortKey, sortDirection, onSortPress } = useSortedRows(
    followers,
    sortValues,
    'followed'
  );

  return (
    <div className={styles.list}>
      <Table
        columns={columns}
        sortKey={sortKey}
        sortDirection={sortDirection}
        onSortPress={onSortPress}
      >
        <TableBody>
          {sorted.map((follower) => (
            <TableRow key={follower.person.id}>
              <TableRowCell>
                <MemberLink person={follower.person} />
              </TableRowCell>
              <RelativeDateCell date={follower.followed} />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </div>
  );
}

export default FollowersTable;
