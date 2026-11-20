import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import Link from 'Components/Link/Link';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { kinds } from 'Helpers/Props';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import { plainText } from '../../portalText';
import { OnboardJob, OnboardResult, OnboardState } from '../../PortalTypes';
import styles from './AddMembers.css';

// How often the page asks how the run is going.
const ASK_EVERY_MS = 1000;

const STATE_WORDS: Record<OnboardState, string> = {
  waiting: 'PortalOnboardWaiting',
  adding: 'PortalOnboardAdding',
  added: 'PortalOnboardAdded',
  failed: 'PortalOnboardFailed',
};

const columns: Column[] = [
  { name: 'name', label: () => translate('PortalMember') },
  { name: 'state', label: () => translate('PortalStatus') },
].map((column) => ({ ...column, isVisible: true, isSortable: false }));

// The run as it is now, asked again every second until it's gone through everyone.
function useRun(id: string) {
  const [run, setRun] = useState<OnboardJob>();

  useEffect(() => {
    let isOver = false;
    const ask = () =>
      callApi<OnboardJob>(`/portal/members/onboard/${id}`).then((answer) => {
        if (!isOver) {
          setRun(answer);
        }

        return answer.isDone;
      });
    const timer = setInterval(() => {
      ask().then((isDone) => isDone && clearInterval(timer));
    }, ASK_EVERY_MS);
    ask();

    return () => {
      isOver = true;
      clearInterval(timer);
    };
  }, [id]);

  return run;
}

function PersonRow({ person }: { person: OnboardResult }) {
  return (
    <TableRow>
      <TableRowCell>{person.name}</TableRowCell>
      <TableRowCell
        className={person.state === 'failed' ? styles.failed : undefined}
      >
        {translate(STATE_WORDS[person.state])}
        {person.message && ` ${plainText(person.message)}`}
      </TableRowCell>
    </TableRow>
  );
}

interface OnboardProgressProps {
  runId: string;
  onRetry: (failed: OnboardResult[]) => void;
}

// Add member's last step: each person as they're onboarded, then who was added and who failed and why, with Retry for
// those who failed.
function OnboardProgress({ runId, onRetry }: OnboardProgressProps) {
  const run = useRun(runId);
  const failed = useMemo(
    () => run?.people.filter((p) => p.state === 'failed') ?? [],
    [run]
  );
  const onRetryPress = useCallback(() => onRetry(failed), [failed, onRetry]);

  if (!run) {
    return null;
  }

  return (
    <>
      {run.isDone && (
        <Alert kind={failed.length ? kinds.WARNING : kinds.SUCCESS}>
          {translate('PortalOnboardDone', {
            added: run.people.length - failed.length,
            failed: failed.length,
          })}
        </Alert>
      )}
      <Table columns={columns}>
        <TableBody>
          {run.people.map((person) => (
            <PersonRow key={person.discordId} person={person} />
          ))}
        </TableBody>
      </Table>
      {run.isDone && (
        <div className={styles.buttons}>
          {failed.length > 0 && (
            <Button kind={kinds.PRIMARY} onPress={onRetryPress}>
              {translate('PortalRetryFailed', { count: failed.length })}
            </Button>
          )}
          <Link to="/members">{translate('PortalBackToMembers')}</Link>
        </div>
      )}
    </>
  );
}

export default OnboardProgress;
