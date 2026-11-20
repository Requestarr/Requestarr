import React, { useCallback, useState } from 'react';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import columnsOf from '../Components/columnsOf';
import PagedSection from '../Components/Paging/PagedSection';
import PortalPage from '../Components/PortalPage';
import TextPromptModal, { reasonPrompt } from '../Components/TextPromptModal';
import { contentTypeText } from '../portalText';
import {
  AccessState,
  ContentType,
  PortalAccessRequest,
  PortalPaused,
} from '../PortalTypes';
import ActionButton from '../Requests/ActionButton';
import usePortal from '../usePortal';
import usePortalAction, { PortalAction } from '../usePortalAction';
import ApproveAccessModal from './ApproveAccessModal';
import styles from '../Portal.css';

const waitingColumns = columnsOf([
  'PortalWho',
  'PortalAskedFor',
  'PortalNote',
  'PortalRequested',
  'PortalActions',
]);

const pausedColumns = columnsOf([
  'PortalWho',
  'PortalPausedSince',
  'PortalActions',
]);

interface WaitingRowProps {
  asked: PortalAccessRequest;
  action: PortalAction;
  onApprove: (asked: PortalAccessRequest) => void;
  onDeny: (asked: PortalAccessRequest) => void;
}

function WaitingRow({ asked, action, onApprove, onDeny }: WaitingRowProps) {
  const onPress = useCallback(
    (name: string) => (name === 'approve' ? onApprove(asked) : onDeny(asked)),
    [asked, onApprove, onDeny]
  );

  return (
    <TableRow>
      <TableRowCell>{asked.name}</TableRowCell>
      <TableRowCell>
        {asked.contentTypes.map(contentTypeText).join(', ')}
      </TableRowCell>
      <TableRowCell>{asked.note ?? ''}</TableRowCell>
      <RelativeDateCell date={asked.requested} />
      <TableRowCell className={styles.actions}>
        <ActionButton
          action="approve"
          isBusy={action.isBusy}
          onPress={onPress}
        />
        <ActionButton action="deny" isBusy={action.isBusy} onPress={onPress} />
      </TableRowCell>
    </TableRow>
  );
}

function PausedRow({
  paused,
  action,
}: {
  paused: PortalPaused;
  action: PortalAction;
}) {
  const onPress = useCallback(
    () => action.act(`paused/${paused.discordId}/unblock`),
    [action, paused]
  );

  return (
    <TableRow>
      <TableRowCell>{paused.name}</TableRowCell>
      <RelativeDateCell date={paused.since} />
      <TableRowCell>
        <ActionButton
          action="unblock"
          isBusy={action.isBusy}
          onPress={onPress}
        />
      </TableRowCell>
    </TableRow>
  );
}

interface WaitingTableProps {
  waiting: PortalAccessRequest[];
  action: PortalAction;
  onApprove: (asked: PortalAccessRequest) => void;
  onDeny: (asked: PortalAccessRequest) => void;
}

function WaitingTable({
  waiting,
  action,
  onApprove,
  onDeny,
}: WaitingTableProps) {
  if (!waiting.length) {
    return (
      <div className={styles.empty}>{translate('PortalNobodyWaiting')}</div>
    );
  }

  return (
    <Table columns={waitingColumns}>
      <TableBody>
        {waiting.map((asked) => (
          <WaitingRow
            key={asked.id}
            asked={asked}
            action={action}
            onApprove={onApprove}
            onDeny={onDeny}
          />
        ))}
      </TableBody>
    </Table>
  );
}

function PausedTable({
  paused,
  action,
}: {
  paused: PortalPaused[];
  action: PortalAction;
}) {
  if (!paused.length) {
    return (
      <div className={styles.empty}>{translate('PortalNobodyPaused')}</div>
    );
  }

  return (
    <Table columns={pausedColumns}>
      <TableBody>
        {paused.map((person) => (
          <PausedRow key={person.discordId} paused={person} action={action} />
        ))}
      </TableBody>
    </Table>
  );
}

// Both lists a page at a time; round counts actions done, so they're read again after each.
function AccessRequestsView({ types }: { types: ContentType[] }) {
  const [round, setRound] = useState(0);
  const onDone = useCallback(() => setRound((current) => current + 1), []);
  const action = usePortalAction(onDone);
  const [approving, setApproving] = useState<PortalAccessRequest>();
  const [denying, setDenying] = useState<PortalAccessRequest>();

  const onClose = useCallback(() => {
    setApproving(undefined);
    setDenying(undefined);
  }, []);
  const onApprove = useCallback(
    (levels: { contentType: ContentType; state: AccessState }[]) => {
      action.act(`access-requests/${approving?.id}/approve`, { levels });
      setApproving(undefined);
    },
    [action, approving]
  );
  const onDeny = useCallback(
    (reason: string) => {
      action.act(`access-requests/${denying?.id}/deny`, { reason });
      setDenying(undefined);
    },
    [action, denying]
  );
  const waitingPage = useCallback(
    (waiting: PortalAccessRequest[]) => (
      <WaitingTable
        waiting={waiting}
        action={action}
        onApprove={setApproving}
        onDeny={setDenying}
      />
    ),
    [action]
  );
  const pausedPage = useCallback(
    (paused: PortalPaused[]) => <PausedTable paused={paused} action={action} />,
    [action]
  );

  return (
    <>
      <ActionOutcomeAlert outcome={action.outcome} />
      <PagedSection
        key={`waiting-${round}`}
        title={translate('PortalWaitingToJoin')}
        path="access-requests/waiting"
      >
        {waitingPage}
      </PagedSection>
      <PagedSection
        key={`paused-${round}`}
        title={translate('PortalPaused')}
        path="access-requests/paused"
      >
        {pausedPage}
      </PagedSection>
      <ApproveAccessModal
        asked={approving}
        types={types}
        onApprove={onApprove}
        onClose={onClose}
      />
      <TextPromptModal
        prompt={
          denying ? reasonPrompt(translate('PortalActionDeny')) : undefined
        }
        onConfirm={onDeny}
        onClose={onClose}
      />
    </>
  );
}

function renderView(types: ContentType[]) {
  return <AccessRequestsView types={types} />;
}

// People asking to join from the welcome channel, and people whose asking is paused: the access request cards and
// paused notices of the admin channel.
function AccessRequestsPage() {
  const result = usePortal<ContentType[]>('types');

  return (
    <PortalPage title={translate('PortalAccessRequests')} result={result}>
      {renderView}
    </PortalPage>
  );
}

export default AccessRequestsPage;
