import React, { useCallback, useMemo, useState } from 'react';
import { useSelect } from 'App/SelectContext';
import Alert from 'Components/Alert';
import TextInput from 'Components/Form/TextInput';
import Label from 'Components/Label';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import SortDirection from 'Helpers/Props/SortDirection';
import translate from 'Utilities/String/translate';
import Person from '../../Components/Person';
import { visibleCells } from '../../Components/PortalList';
import SelectCell from '../../Components/Select/SelectCell';
import { PortalServerMember } from '../../PortalTypes';
import styles from './AddMembers.css';

// A server member as a row. Servarr's select state keys rows by a number, and a Discord ID is too long for one, so a
// row's ID is its place in the server's list.
export type Candidate = PortalServerMember & { id: number };

// What the filter box matches: their name or Discord username, as you type.
function matches(candidate: Candidate, words: string) {
  const wanted = words.trim().toLowerCase();

  return (
    !wanted ||
    candidate.name.toLowerCase().includes(wanted) ||
    candidate.username.toLowerCase().includes(wanted)
  );
}

interface CandidateRowProps {
  candidate: Candidate;
  columns: Column[];
}

function CandidateRow({ candidate, columns }: CandidateRowProps) {
  return (
    <TableRow>
      <SelectCell id={candidate.id} />
      {visibleCells(columns, {
        name: (
          <TableRowCell>
            <Person
              name={candidate.name}
              handle={candidate.username}
              avatar={candidate.avatar}
            />
          </TableRowCell>
        ),
        joined: <RelativeDateCell date={candidate.joined ?? ''} />,
        roles: (
          <TableRowCell>
            {candidate.roles.map((role) => (
              <Label key={role}>{role}</Label>
            ))}
          </TableRowCell>
        ),
        importedRequests: (
          <TableRowCell>
            {candidate.importedRequests > 0 &&
              translate('PortalImportedRequests', {
                count: candidate.importedRequests,
              })}
          </TableRowCell>
        ),
      })}
    </TableRow>
  );
}

// The header box's state and change for the people shown: a pick the box or a filter hides stays picked, as Sonarr's own
// select state keeps picks off the page.
function useSelectShown(shown: Candidate[]) {
  const [{ selectedState }, selectDispatch] = useSelect();
  const onSelectAllChange = useCallback(
    ({ value }: { value: boolean }) =>
      shown.forEach(({ id }) =>
        selectDispatch({
          type: 'toggleSelected',
          id,
          isSelected: value,
          shiftKey: false,
        })
      ),
    [shown, selectDispatch]
  );

  return {
    allSelected: shown.length > 0 && shown.every((c) => selectedState[c.id]),
    allUnselected: !shown.some((c) => selectedState[c.id]),
    onSelectAllChange,
  };
}

interface AddMembersTableProps {
  candidates: Candidate[];
  columns: Column[];
  sortKey: string;
  sortDirection: SortDirection;
  onSortPress: (sortKey: string) => void;
}

// The people to pick from, as Sonarr's Activity, then Blocklist lists its items: a box on every row and one in the header
// for everyone shown, narrowed as you type (Prowlarr's AddIndexerModalContent has the same box) and sorted by any column.
function AddMembersTable({
  candidates,
  columns,
  sortKey,
  sortDirection,
  onSortPress,
}: AddMembersTableProps) {
  const [words, setWords] = useState('');
  const shown = useMemo(
    () => candidates.filter((candidate) => matches(candidate, words)),
    [candidates, words]
  );
  const selection = useSelectShown(shown);
  const onWordsChange = useCallback(
    ({ value }: { value: string }) => setWords(value),
    []
  );

  return (
    <>
      <TextInput
        className={styles.filter}
        placeholder={translate('PortalFilterPeople')}
        name="filter"
        value={words}
        onChange={onWordsChange}
      />

      {shown.length ? (
        <Table
          selectAll={true}
          columns={columns}
          sortKey={sortKey}
          sortDirection={sortDirection}
          onSortPress={onSortPress}
          {...selection}
        >
          <TableBody>
            {shown.map((candidate) => (
              <CandidateRow
                key={candidate.id}
                candidate={candidate}
                columns={columns}
              />
            ))}
          </TableBody>
        </Table>
      ) : (
        <Alert kind={kinds.INFO}>{translate('PortalNoPeopleMatch')}</Alert>
      )}
    </>
  );
}

export default AddMembersTable;
