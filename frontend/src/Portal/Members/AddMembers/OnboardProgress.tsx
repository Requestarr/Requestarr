import React, { useCallback, useEffect, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
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
import { ActionOutcome } from '../../usePortalAction';
import onboardTitle from './onboardTitle';
import { Who } from './useStartRun';
import styles from './AddMembers.css';

// How often the window asks how the run is going.
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

// The run as it is now, asked again a second after each answer until it's gone through everyone; isLost once it can't
// be read (Requestarr restarted and forgot it).
function useRun(id: string) {
  const [run, setRun] = useState<OnboardJob>();
  const [isLost, setIsLost] = useState(false);

  useEffect(() => {
    let isOver = false;
    let timer: ReturnType<typeof setTimeout> | undefined = undefined;
    const ask = () =>
      callApi<OnboardJob>(`/portal/members/onboard/${id}`).then(
        (answer) => {
          if (!isOver) {
            setRun(answer);
            timer = answer.isDone ? timer : setTimeout(ask, ASK_EVERY_MS);
          }
        },
        () => !isOver && setIsLost(true)
      );
    ask();

    return () => {
      isOver = true;
      clearTimeout(timer);
    };
  }, [id]);

  return { run, isLost };
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
  people: Who[];
  runId: string;
  refused?: ActionOutcome;
  isStarting: boolean;
  onRetry: (failed: OnboardResult[]) => void;
  onClose: () => void;
}

// The Onboard window's last step: each person as they're onboarded, then who was added and who failed and why, with
// Retry for those who failed.
function OnboardProgress(props: OnboardProgressProps) {
  const { people, runId, refused, isStarting, onRetry, onClose } = props;
  const { run, isLost } = useRun(runId);
  const failed = useMemo(
    () => run?.people.filter((p) => p.state === 'failed') ?? [],
    [run]
  );
  const onRetryPress = useCallback(() => onRetry(failed), [failed, onRetry]);

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{onboardTitle(people)}</ModalHeader>

      <ModalBody>
        {refused && (
          <Alert kind={kinds.DANGER}>{plainText(refused.message)}</Alert>
        )}

        {isLost && (
          <Alert kind={kinds.WARNING}>{translate('PortalOnboardLost')}</Alert>
        )}

        {!run && !isLost && <LoadingIndicator />}

        {run?.isDone && (
          <Alert kind={failed.length ? kinds.WARNING : kinds.SUCCESS}>
            {translate('PortalOnboardDone', {
              added: run.people.length - failed.length,
              failed: failed.length,
            })}
          </Alert>
        )}

        {run && !isLost && (
          <Table columns={columns}>
            <TableBody>
              {run.people.map((person) => (
                <PersonRow key={person.discordId} person={person} />
              ))}
            </TableBody>
          </Table>
        )}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onClose}>{translate('Close')}</Button>

        {run?.isDone && failed.length > 0 && (
          <SpinnerButton
            kind={kinds.PRIMARY}
            isSpinning={isStarting}
            isDisabled={isStarting}
            onPress={onRetryPress}
          >
            {translate('PortalRetryFailed', { count: failed.length })}
          </SpinnerButton>
        )}
      </ModalFooter>
    </ModalContent>
  );
}

export default OnboardProgress;
