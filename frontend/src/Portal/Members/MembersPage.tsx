import React, { useCallback, useMemo, useState } from 'react';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Avatar from '../Components/Avatar';
import PortalFilter, { ALL, optionsOf } from '../Components/PortalFilter';
import PortalPage from '../Components/PortalPage';
import PortalToolbar from '../Components/PortalToolbar';
import {
  accessText,
  byTypeText,
  contentTypeText,
  memberStatusText,
} from '../portalText';
import { ContentType, PortalMember } from '../PortalTypes';
import usePortal from '../usePortal';
import useSortedRows, { SortValues } from '../useSortedRows';
import AddMemberModal from './AddMemberModal';
import styles from '../Portal.css';

const ACCESS_PREFIX = 'access-';

function accessFor(member: PortalMember, type: ContentType) {
  const access = member.access.find((a) => a.contentType === type);
  return access ? accessText(access) : translate('PortalAccessOff');
}

// The content types any member has a setting for, in the order Requestarr lists them.
function typesOf(members: PortalMember[]) {
  const order: ContentType[] = ['movie', 'tv', 'music', 'comic', 'book'];
  const present = new Set(
    members.flatMap((m) => m.access.map((a) => a.contentType))
  );

  return order.filter((type) => present.has(type));
}

function columnsFor(types: ContentType[]): Column[] {
  const column = (name: string, label: string) => ({
    name,
    label,
    isVisible: true,
    isSortable: true,
  });

  return [
    column('name', translate('PortalMember')),
    column('status', translate('PortalStatus')),
    ...types.map((type) =>
      column(`${ACCESS_PREFIX}${type}`, contentTypeText(type))
    ),
    column('requests', translate('PortalRequests')),
    column('active', translate('PortalActive')),
    column('pending', translate('PortalPending')),
    column('following', translate('PortalFollowing')),
    column('byType', translate('PortalByType')),
    column('firstRequest', translate('PortalFirstRequest')),
    column('lastRequest', translate('PortalLastRequest')),
    column('added', translate('PortalAdded')),
  ];
}

function sortValuesFor(types: ContentType[]): SortValues<PortalMember> {
  const values: SortValues<PortalMember> = {
    name: (m) => m.name,
    status: (m) => memberStatusText(m.status),
    requests: (m) => m.requests,
    active: (m) => m.active,
    pending: (m) => m.pending,
    following: (m) => m.following,
    byType: (m) => m.requests,
    firstRequest: (m) => m.firstRequest,
    lastRequest: (m) => m.lastRequest,
    added: (m) => m.added,
  };

  types.forEach((type) => {
    values[`${ACCESS_PREFIX}${type}`] = (m) => accessFor(m, type);
  });

  return values;
}

function MemberRow({
  member,
  types,
}: {
  member: PortalMember;
  types: ContentType[];
}) {
  return (
    <TableRow>
      <TableRowCell>
        <Link to={`/members/${member.id}`}>
          <Avatar url={member.avatar} />
          {member.name}
        </Link>
        {member.discordName && (
          <div className={styles.muted}>@{member.discordName}</div>
        )}
      </TableRowCell>
      <TableRowCell>{memberStatusText(member.status)}</TableRowCell>
      {types.map((type) => (
        <TableRowCell key={type}>{accessFor(member, type)}</TableRowCell>
      ))}
      <TableRowCell>{member.requests}</TableRowCell>
      <TableRowCell>{member.active}</TableRowCell>
      <TableRowCell>{member.pending}</TableRowCell>
      <TableRowCell>{member.following}</TableRowCell>
      <TableRowCell>{byTypeText(member)}</TableRowCell>
      <RelativeDateCell date={member.firstRequest ?? ''} />
      <RelativeDateCell date={member.lastRequest ?? ''} />
      <RelativeDateCell date={member.added} />
    </TableRow>
  );
}

function MembersList({ members }: { members: PortalMember[] }) {
  const [status, setStatus] = useState(ALL);
  const types = useMemo(() => typesOf(members), [members]);
  const values = useMemo(() => sortValuesFor(types), [types]);
  const statuses = useMemo(
    () =>
      optionsOf(members, (m) => ({
        key: m.status,
        value: memberStatusText(m.status),
      })),
    [members]
  );
  const shown = useMemo(
    () => members.filter((m) => status === ALL || m.status === status),
    [members, status]
  );
  const { sorted, sortKey, sortDirection, onSortPress } = useSortedRows(
    shown,
    values,
    'name'
  );

  const onFilterChange = useCallback(
    (_name: string, value: string) => setStatus(value),
    []
  );

  return (
    <>
      <div className={styles.filters}>
        <PortalFilter
          name="status"
          label={translate('PortalStatus')}
          value={status}
          options={statuses}
          onChange={onFilterChange}
        />
      </div>
      <Table
        columns={columnsFor(types)}
        sortKey={sortKey}
        sortDirection={sortDirection}
        onSortPress={onSortPress}
      >
        <TableBody>
          {sorted.map((member) => (
            <MemberRow key={member.id} member={member} types={types} />
          ))}
        </TableBody>
      </Table>
    </>
  );
}

function renderMembers(members: PortalMember[]) {
  return members.length ? (
    <MembersList members={members} />
  ) : (
    <div className={styles.empty}>{translate('PortalNoMembers')}</div>
  );
}

// Every member Requestarr knows: their access to each type with its limit, and how many requests they have; Add member
// onboards someone from the Discord server.
function MembersPage() {
  const result = usePortal<PortalMember[]>('members');
  const onActionDone = result.reload;
  const [isAdding, setIsAdding] = useState(false);
  const onAddPress = useCallback(() => setIsAdding(true), []);
  const onAddClose = useCallback(() => setIsAdding(false), []);
  const toolbar = (
    <PortalToolbar
      label={translate('PortalActionAddMember')}
      iconName={icons.ADD}
      onPress={onAddPress}
    />
  );

  return (
    <>
      <PortalPage
        title={translate('PortalMembers')}
        result={result}
        toolbar={toolbar}
      >
        {renderMembers}
      </PortalPage>
      <AddMemberModal
        isOpen={isAdding}
        onClose={onAddClose}
        onDone={onActionDone}
      />
    </>
  );
}

export default MembersPage;
