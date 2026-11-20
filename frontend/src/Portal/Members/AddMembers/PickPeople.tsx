import React, { useCallback, useMemo, useState } from 'react';
import Label from 'Components/Label';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import Person from '../../Components/Person';
import RowCheckbox from '../../Components/Selection/RowCheckbox';
import SelectionBar from '../../Components/Selection/SelectionBar';
import useSelection, {
  Selection,
} from '../../Components/Selection/useSelection';
import { PortalServerMember } from '../../PortalTypes';
import useSortedRows, { SortValues } from '../../useSortedRows';
import styles from './AddMembers.css';

// A server member as a row: its ID is their Discord ID.
type Candidate = PortalServerMember & { id: string };

const columns: Column[] = [
  { name: 'name', label: () => translate('PortalMember') },
  { name: 'joined', label: () => translate('PortalJoinedServer') },
  { name: 'roles', label: () => translate('PortalServerRoles') },
  { name: 'imported', label: () => translate('PortalImportedHistory') },
].map((column) => ({
  ...column,
  isVisible: true,
  isSortable: column.name !== 'roles',
}));

const sortValues: SortValues<Candidate> = {
  name: (c) => c.name,
  joined: (c) => c.joined,
  imported: (c) => c.importedRequests,
};

// What the filter box matches: their name or Discord username, as you type.
function matches(candidate: Candidate, words: string) {
  const wanted = words.trim().toLowerCase();

  return (
    !wanted ||
    candidate.name.toLowerCase().includes(wanted) ||
    candidate.username.toLowerCase().includes(wanted)
  );
}

function CandidateRow({
  candidate,
  selection,
}: {
  candidate: Candidate;
  selection: Selection;
}) {
  return (
    <TableRow>
      <TableRowCell>
        <RowCheckbox id={candidate.id} selection={selection} />
      </TableRowCell>
      <TableRowCell>
        <Person
          name={candidate.name}
          handle={candidate.username}
          avatar={candidate.avatar}
        />
      </TableRowCell>
      <RelativeDateCell date={candidate.joined ?? ''} />
      <TableRowCell>
        {candidate.roles.map((role) => (
          <Label key={role}>{role}</Label>
        ))}
      </TableRowCell>
      <TableRowCell>
        {candidate.importedRequests > 0 &&
          translate('PortalImportedRequests', {
            count: candidate.importedRequests,
          })}
      </TableRowCell>
    </TableRow>
  );
}

interface PickPeopleProps {
  people: PortalServerMember[];
  // Someone picked to begin with (a member page's Onboard), by Discord ID.
  pick: string | null;
  onNext: (picked: PortalServerMember[]) => void;
}

// Add member's first step: everyone in the server who isn't onboarded, people with imported history first, narrowed
// as you type and sorted by any column; pick one or many, then set them up.
function PickPeople({ people, pick, onNext }: PickPeopleProps) {
  const [words, setWords] = useState('');
  const candidates = useMemo(
    () => people.map((person) => ({ ...person, id: person.discordId })),
    [people]
  );
  const shown = useMemo(
    () => candidates.filter((candidate) => matches(candidate, words)),
    [candidates, words]
  );
  const sort = useSortedRows(
    shown,
    sortValues,
    'imported',
    sortDirections.DESCENDING
  );
  const selection = useSelection(sort.sorted, sort.sorted, pick ? [pick] : []);
  const onWords = useCallback(
    (event: React.ChangeEvent<HTMLInputElement>) =>
      setWords(event.target.value),
    []
  );
  const onSetUp = useCallback(
    () => onNext(selection.picked),
    [selection.picked, onNext]
  );
  const actions = [
    {
      name: 'setUp',
      label: translate('PortalSetUpPeople', { count: selection.picked.length }),
      isDanger: false,
      whyNot: null,
    },
  ];

  return (
    <>
      <input
        className={styles.filter}
        placeholder={translate('PortalFilterPeople')}
        value={words}
        onChange={onWords}
      />
      <SelectionBar
        selection={selection}
        actions={actions}
        isBusy={false}
        onAction={onSetUp}
      />
      <Table
        selectAll={true}
        allSelected={selection.allOnPage}
        allUnselected={selection.noneOnPage}
        columns={columns}
        sortKey={sort.sortKey}
        sortDirection={sort.sortDirection}
        onSelectAllChange={selection.onPageChange}
        onSortPress={sort.onSortPress}
      >
        <TableBody>
          {sort.sorted.map((candidate) => (
            <CandidateRow
              key={candidate.id}
              candidate={candidate}
              selection={selection}
            />
          ))}
        </TableBody>
      </Table>
    </>
  );
}

export default PickPeople;
