import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import columnsOf from '../Components/columnsOf';
import PortalPage from '../Components/PortalPage';
import TextPromptModal, { reasonPrompt } from '../Components/TextPromptModal';
import { contentTypeText } from '../portalText';
import {
  AccessState,
  ContentType,
  PortalAccessRequest,
  PortalAccessRequests,
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
  return (
    <FieldSet legend={translate('PortalWaitingToJoin')}>
      {waiting.length ? (
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
      ) : (
        <div className={styles.empty}>{translate('PortalNobodyWaiting')}</div>
      )}
    </FieldSet>
  );
}

function PausedTable({
  paused,
  action,
}: {
  paused: PortalPaused[];
  action: PortalAction;
}) {
  return (
    <FieldSet legend={translate('PortalPaused')}>
      {paused.length ? (
        <Table columns={pausedColumns}>
          <TableBody>
            {paused.map((person) => (
              <PausedRow
                key={person.discordId}
                paused={person}
                action={action}
              />
            ))}
          </TableBody>
        </Table>
      ) : (
        <div className={styles.empty}>{translate('PortalNobodyPaused')}</div>
      )}
    </FieldSet>
  );
}

function AccessRequestsView({
  data,
  onDone,
}: {
  data: PortalAccessRequests;
  onDone: () => void;
}) {
  const action = usePortalAction(onDone);
  const [approving, setApproving] = useState<PortalAccessRequest>();
  const [denying, setDenying] = useState<PortalAccessRequest>();
  const { data: offered } = usePortal<ContentType[]>('types');
  const types = offered ?? [];

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

  return (
    <>
      <ActionOutcomeAlert outcome={action.outcome} />
      <WaitingTable
        waiting={data.waiting}
        action={action}
        onApprove={setApproving}
        onDeny={setDenying}
      />
      <PausedTable paused={data.paused} action={action} />
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

// People asking to join from the welcome channel, and people whose asking is paused: the access request cards and
// paused notices of the admin channel.
function AccessRequestsPage() {
  const result = usePortal<PortalAccessRequests>('access-requests');
  const onActionDone = result.reload;
  const render = useCallback(
    (data: PortalAccessRequests) => (
      <AccessRequestsView data={data} onDone={onActionDone} />
    ),
    [onActionDone]
  );

  return (
    <PortalPage title={translate('PortalAccessRequests')} result={result}>
      {render}
    </PortalPage>
  );
}

export default AccessRequestsPage;
