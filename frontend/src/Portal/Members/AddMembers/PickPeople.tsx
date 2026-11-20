import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { SelectProvider, useSelect } from 'App/SelectContext';
import Label from 'Components/Label';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { sortDirections } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import Person from '../../Components/Person';
import SelectCell from '../../Components/Select/SelectCell';
import SelectFooter from '../../Components/Select/SelectFooter';
import { PortalServerMember } from '../../PortalTypes';
import useSortedRows, { SortValues } from '../../useSortedRows';
import styles from './AddMembers.css';

// A server member as a row. Servarr's select state keys rows by a number, and a Discord ID is too long for one, so a
// row's ID is its place in the list.
type Candidate = PortalServerMember & { id: number };

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

function CandidateRow({ candidate }: { candidate: Candidate }) {
  return (
    <TableRow>
      <SelectCell id={candidate.id} />
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

// The people picked, in the list's order, and the header box's state and change for the people the filter shows: a
// pick the filter hides stays picked, as Sonarr's own select state keeps picks off the page.
function usePicks(candidates: Candidate[], shown: Candidate[], kept: string[]) {
  const [selectState, selectDispatch] = useSelect();
  const { selectedState } = selectState;
  const picked = useMemo(() => {
    const ids = getSelectedIds(selectedState);

    return candidates.filter((candidate) => ids.includes(candidate.id));
  }, [candidates, selectedState]);
  const pickShown = useCallback(
    (isSelected: boolean, among: Candidate[]) =>
      among.forEach((candidate) =>
        selectDispatch({
          type: 'toggleSelected',
          id: candidate.id,
          isSelected,
          shiftKey: false,
        })
      ),
    [selectDispatch]
  );
  const onSelectAllChange = useCallback(
    ({ value }: { value: boolean }) => pickShown(value, shown),
    [pickShown, shown]
  );

  // Who was picked before (a member page's Onboard, or Back), once the rows are there.
  useEffect(() => {
    pickShown(
      true,
      candidates.filter((candidate) => kept.includes(candidate.discordId))
    );
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only the first picks, not again on every change
  }, []);

  return {
    picked,
    allSelected: shown.length > 0 && shown.every((c) => selectedState[c.id]),
    allUnselected: !shown.some((c) => selectedState[c.id]),
    onSelectAllChange,
  };
}

interface PickPeopleProps {
  people: PortalServerMember[];
  // Who's picked to begin with (a member page's Onboard, or the picks before Back), by Discord ID.
  kept: string[];
  onNext: (picked: PortalServerMember[]) => void;
}

// The list to pick from: a box on every row and one for everyone the filter shows (a pick the filter hides stays
// picked), and the bar to set up the people picked.
function PickList({
  candidates,
  kept,
  onNext,
}: PickPeopleProps & { candidates: Candidate[] }) {
  const [words, setWords] = useState('');
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
  const selection = usePicks(candidates, sort.sorted, kept);
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
      isOffered: selection.picked.length > 0,
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
      <Table
        selectAll={true}
        allSelected={selection.allSelected}
        allUnselected={selection.allUnselected}
        columns={columns}
        sortKey={sort.sortKey}
        sortDirection={sort.sortDirection}
        onSelectAllChange={selection.onSelectAllChange}
        onSortPress={sort.onSortPress}
      >
        <TableBody>
          {sort.sorted.map((candidate) => (
            <CandidateRow key={candidate.id} candidate={candidate} />
          ))}
        </TableBody>
      </Table>
      <SelectFooter
        actions={actions}
        isBusy={false}
        selectedText={translate('PortalPeopleToAdd', { count: shown.length })}
        onAction={onSetUp}
      />
    </>
  );
}

// Add member's first step: everyone in the server who isn't onboarded, people with imported history first, narrowed
// as you type and sorted by any column; pick one or many, then set them up.
function PickPeople(props: PickPeopleProps) {
  const candidates = useMemo(
    () => props.people.map((person, index) => ({ ...person, id: index })),
    [props.people]
  );

  return (
    <SelectProvider items={candidates}>
      <PickList {...props} candidates={candidates} />
    </SelectProvider>
  );
}

export default PickPeople;
