import React from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import MemberLink from '../Components/MemberLink';
import { visibleCells } from '../Components/PortalList';
import { PortalFollower } from '../PortalTypes';

interface FollowerRowProps {
  follower: PortalFollower;
  columns: Column[];
}

// Someone following a request, with their picture, name and @handle, and when they followed.
function FollowerRow({ follower, columns }: FollowerRowProps) {
  return (
    <TableRow>
      {visibleCells(columns, {
        member: (
          <TableRowCell>
            <MemberLink person={follower.person} />
          </TableRowCell>
        ),
        followed: <RelativeDateCell date={follower.followed} />,
      })}
    </TableRow>
  );
}

export default FollowerRow;
