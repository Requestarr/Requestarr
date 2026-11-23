import React, { useCallback, useEffect, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import Person from 'Portal/Components/Person';
import translate from 'Utilities/String/translate';
import { callRequestarrApi } from '../../requestarrApi';
import { AdminCandidate } from '../../RequestarrSettingsTypes';
import useApiAction from '../../useApiAction';

const PATH = 'discord/admins';

const columns: Column[] = [
  {
    name: 'name',
    label: () => translate('PortalMember'),
    isVisible: true,
  },
];

interface AdminRowProps {
  candidate: AdminCandidate;
  isPicked: boolean;
  isReadOnly: boolean;
  onPick: (discordId: string, isPicked: boolean) => void;
}

// Someone in the server, as Add member lists them (picture, name, @handle), with a box: ticked holds the admin
// role. The server's owner is always ticked.
function AdminRow({ candidate, isPicked, isReadOnly, onPick }: AdminRowProps) {
  const onSelectedChange = useCallback(
    ({ value }: { value: boolean }) => onPick(candidate.discordId, value),
    [candidate.discordId, onPick]
  );

  return (
    <TableRow>
      <TableSelectCell
        id={candidate.discordId}
        isSelected={isPicked || candidate.isOwner}
        isDisabled={isReadOnly || candidate.isOwner}
        onSelectedChange={onSelectedChange}
      />
      <TableRowCell>
        <Person
          name={candidate.name}
          handle={candidate.username}
          avatar={candidate.avatar}
        />
      </TableRowCell>
    </TableRow>
  );
}

// Who's an admin: the people in Requestarr's server with Requestarr's admin role, picked in the web app so nobody
// has to make or copy a role in Discord. Saving gives and takes the role there.
function AdminPicker({ isReadOnly }: { isReadOnly: boolean }) {
  const [candidates, setCandidates] = useState<AdminCandidate[]>();
  const [picked, setPicked] = useState<Set<string>>(new Set());
  const [isSaved, setIsSaved] = useState(false);
  const action = useApiAction();
  const { run } = action;

  const show = useCallback((list: AdminCandidate[]) => {
    setCandidates(list);
    setPicked(new Set(list.filter((c) => c.isAdmin).map((c) => c.discordId)));
  }, []);

  useEffect(() => {
    run(() => callRequestarrApi<AdminCandidate[]>(PATH), show);
  }, [run, show]);

  const onPick = useCallback((discordId: string, isPicked: boolean) => {
    setIsSaved(false);
    setPicked((current) => {
      const next = new Set(current);

      if (isPicked) {
        next.add(discordId);
      } else {
        next.delete(discordId);
      }

      return next;
    });
  }, []);

  const onSave = useCallback(() => {
    run(
      () =>
        callRequestarrApi<AdminCandidate[]>(PATH, 'PUT', { ids: [...picked] }),
      (list) => {
        show(list);
        setIsSaved(true);
      }
    );
  }, [run, picked, show]);

  return (
    <FieldSet legend={translate('SetupAdmins')}>
      <p>{translate('SetupAdminsIntro')}</p>
      {!candidates && !action.problem && <LoadingIndicator />}
      {action.problem && <Alert kind={kinds.DANGER}>{action.problem}</Alert>}
      {isSaved && (
        <Alert kind={kinds.SUCCESS}>{translate('SetupAdminsSaved')}</Alert>
      )}
      {candidates && (
        <Table columns={columns}>
          <TableBody>
            {candidates.map((candidate) => (
              <AdminRow
                key={candidate.discordId}
                candidate={candidate}
                isPicked={picked.has(candidate.discordId)}
                isReadOnly={isReadOnly}
                onPick={onPick}
              />
            ))}
          </TableBody>
        </Table>
      )}
      <SpinnerButton
        kind={kinds.PRIMARY}
        isSpinning={action.isBusy}
        isDisabled={isReadOnly || !candidates}
        onPress={onSave}
      >
        {translate('Save')}
      </SpinnerButton>
    </FieldSet>
  );
}

export default AdminPicker;
