import React, { useCallback, useMemo, useState } from 'react';
import { useHistory } from 'react-router-dom';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import getPathWithUrlBase from 'Utilities/getPathWithUrlBase';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import PortalPager from '../Components/Paging/PortalPager';
import usePagedRows from '../Components/Paging/usePagedRows';
import Person from '../Components/Person';
import PortalFilter, { ALL, optionsOf } from '../Components/PortalFilter';
import PortalPage from '../Components/PortalPage';
import PortalToolbar from '../Components/PortalToolbar';
import RowCheckbox from '../Components/Selection/RowCheckbox';
import SelectionBar from '../Components/Selection/SelectionBar';
import useSelection, { Selection } from '../Components/Selection/useSelection';
import {
  accessText,
  byTypeText,
  contentTypeText,
  memberStatusText,
} from '../portalText';
import { ContentType, PortalMember } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import usePortal from '../usePortal';
import useSortedRows, { SortValues } from '../useSortedRows';
import { BULK_MEMBER_ACTIONS, memberActions } from './memberActionRules';
import MemberRowActions from './MemberRowActions';
import useMemberActions from './useMemberActions';
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
    { name: 'actions', label: '', isVisible: true, isSortable: false },
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

interface MemberRowProps {
  member: PortalMember;
  types: ContentType[];
  selection: Selection;
  isBusy: boolean;
  onAction: (members: PortalMember[], action: string) => void;
}

function MemberRow({
  member,
  types,
  selection,
  isBusy,
  onAction,
}: MemberRowProps) {
  return (
    <TableRow>
      <TableRowCell>
        <RowCheckbox id={member.id} selection={selection} />
      </TableRowCell>
      <TableRowCell>
        <Link to={`/members/${member.id}`}>
          <Person
            name={member.name}
            handle={member.discordName}
            avatar={member.avatar}
          />
        </Link>
      </TableRowCell>
      <TableRowCell>{memberStatusText(member.status)}</TableRowCell>
      <TableRowCell>
        <MemberRowActions member={member} isBusy={isBusy} onAction={onAction} />
      </TableRowCell>
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

interface MembersListProps {
  members: PortalMember[];

  // After an action: the list as it is now.
  onDone: () => void;
}

// The Status filter's choices, what it's set to, and the members it lets through.
function useStatusFilter(members: PortalMember[]) {
  const [status, setStatus] = useState(ALL);
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
  const onFilterChange = useCallback(
    (_name: string, value: string) => setStatus(value),
    []
  );

  return { status, statuses, shown, onFilterChange };
}

// The members picked and what the bar does to them (suspend, unsuspend, change access), each action clearing the picks
// once it's done.
function useMemberBulk(
  sorted: PortalMember[],
  pageRows: PortalMember[],
  onDone: () => void
) {
  const selection = useSelection(sorted, pageRows);
  const { picked, onClear } = selection;
  const onActionDone = useCallback(() => {
    onClear();
    onDone();
  }, [onClear, onDone]);
  const { run, ask, isBusy, outcome, dialogs } = useMemberActions(onActionDone);
  const barActions = useMemo(
    () =>
      BULK_MEMBER_ACTIONS.map((name) => ({
        name,
        label: actionLabel(name),
        isDanger: false,
        isOffered: picked.every((m) => memberActions(m).includes(name)),
      })),
    [picked]
  );
  const onBulk = useCallback(
    (name: string) => ask(picked, name),
    [ask, picked]
  );

  return { selection, run, isBusy, outcome, dialogs, barActions, onBulk };
}

function MembersList({ members, onDone }: MembersListProps) {
  const types = useMemo(() => typesOf(members), [members]);
  const values = useMemo(() => sortValuesFor(types), [types]);
  const { status, statuses, shown, onFilterChange } = useStatusFilter(members);
  const { sorted, sortKey, sortDirection, onSortPress } = useSortedRows(
    shown,
    values,
    'name'
  );
  const { pageRows, pager } = usePagedRows(
    sorted,
    `${status} ${sortKey} ${sortDirection}`
  );
  const { selection, run, isBusy, outcome, dialogs, barActions, onBulk } =
    useMemberBulk(sorted, pageRows, onDone);

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
      <ActionOutcomeAlert outcome={outcome} />
      <SelectionBar
        selection={selection}
        actions={barActions}
        isBusy={isBusy}
        summary={translate('PortalShowingMembers', {
          shown: sorted.length,
          total: members.length,
        })}
        onAction={onBulk}
      />
      <Table
        selectAll={true}
        allSelected={selection.allOnPage}
        allUnselected={selection.noneOnPage}
        columns={columnsFor(types)}
        sortKey={sortKey}
        sortDirection={sortDirection}
        onSelectAllChange={selection.onPageChange}
        onSortPress={onSortPress}
      >
        <TableBody>
          {pageRows.map((member) => (
            <MemberRow
              key={member.id}
              member={member}
              types={types}
              selection={selection}
              isBusy={isBusy}
              onAction={run}
            />
          ))}
        </TableBody>
      </Table>
      <PortalPager {...pager} />
      {dialogs}
    </>
  );
}

// Every member Requestarr knows: their access to each type with its limit, and how many requests they have. Each row
// has its actions and a box to pick it, for acting on several at once; Add member lists the server's people to onboard.
function MembersPage() {
  const history = useHistory();
  const result = usePortal<PortalMember[]>('members');
  const onActionDone = result.reload;
  const onAddPress = useCallback(
    () => history.push(getPathWithUrlBase('/members/add')),
    [history]
  );
  const renderMembers = useCallback(
    (members: PortalMember[]) =>
      members.length ? (
        <MembersList members={members} onDone={onActionDone} />
      ) : (
        <div className={styles.empty}>{translate('PortalNoMembers')}</div>
      ),
    [onActionDone]
  );
  const toolbar = (
    <PortalToolbar
      label={translate('PortalActionAddMember')}
      iconName={icons.ADD}
      onPress={onAddPress}
    />
  );

  return (
    <PortalPage
      title={translate('PortalMembers')}
      result={result}
      toolbar={toolbar}
    >
      {renderMembers}
    </PortalPage>
  );
}

export default MembersPage;
