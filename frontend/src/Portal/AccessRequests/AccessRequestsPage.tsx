import React, { useCallback, useState } from 'react';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import PortalList, { visibleCells } from '../Components/PortalList';
import TextConfirmModal, { reasonPrompt } from '../Components/TextConfirmModal';
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

interface WaitingRowProps {
  asked: PortalAccessRequest;
  columns: Column[];
  action: PortalAction;
  onApprove: (asked: PortalAccessRequest) => void;
  onDeny: (asked: PortalAccessRequest) => void;
}

function WaitingRow(props: WaitingRowProps) {
  const { asked, columns, action, onApprove, onDeny } = props;
  const onPress = useCallback(
    (name: string) => (name === 'approve' ? onApprove(asked) : onDeny(asked)),
    [asked, onApprove, onDeny]
  );

  return (
    <TableRow>
      {visibleCells(columns, {
        who: <TableRowCell>{asked.name}</TableRowCell>,
        askedFor: (
          <TableRowCell>
            {asked.contentTypes.map(contentTypeText).join(', ')}
          </TableRowCell>
        ),
        note: <TableRowCell>{asked.note ?? ''}</TableRowCell>,
        requested: <RelativeDateCell date={asked.requested} />,
        actions: (
          <TableRowCell className={styles.actions}>
            <ActionButton
              action="approve"
              isBusy={action.isBusy}
              onPress={onPress}
            />
            <ActionButton
              action="deny"
              isBusy={action.isBusy}
              onPress={onPress}
            />
          </TableRowCell>
        ),
      })}
    </TableRow>
  );
}

function PausedRow({
  paused,
  columns,
  action,
}: {
  paused: PortalPaused;
  columns: Column[];
  action: PortalAction;
}) {
  const onPress = useCallback(
    () => action.act(`paused/${paused.discordId}/unblock`),
    [action, paused]
  );

  return (
    <TableRow>
      {visibleCells(columns, {
        who: <TableRowCell>{paused.name}</TableRowCell>,
        since: <RelativeDateCell date={paused.since} />,
        actions: (
          <TableRowCell>
            <ActionButton
              action="unblock"
              isBusy={action.isBusy}
              onPress={onPress}
            />
          </TableRowCell>
        ),
      })}
    </TableRow>
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
  const waitingRow = useCallback(
    (asked: PortalAccessRequest, columns: Column[]) => (
      <WaitingRow
        asked={asked}
        columns={columns}
        action={action}
        onApprove={setApproving}
        onDeny={setDenying}
      />
    ),
    [action]
  );
  const pausedRow = useCallback(
    (paused: PortalPaused, columns: Column[]) => (
      <PausedRow paused={paused} columns={columns} action={action} />
    ),
    [action]
  );

  return (
    <>
      <PortalList
        name="accessWaiting"
        title={translate('PortalWaitingToJoin')}
        emptyText={translate('PortalNobodyWaiting')}
        round={round}
        renderRow={waitingRow}
      />
      <PortalList
        name="accessPaused"
        title={translate('PortalPaused')}
        emptyText={translate('PortalNobodyPaused')}
        round={round}
        renderRow={pausedRow}
      />
      <ApproveAccessModal
        asked={approving}
        types={types}
        onApprove={onApprove}
        onClose={onClose}
      />
      <TextConfirmModal
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
    <PageContent title={translate('PortalAccessRequests')}>
      <PageContentBody>
        {result.isNotFound ? (
          <NotFound message={translate('PortalNotFound')} />
        ) : (
          <PageSectionContent
            isFetching={result.isLoading}
            isPopulated={result.data !== undefined}
            error={result.hasError ? result : undefined}
            errorMessage={translate('PortalLoadError')}
          >
            {result.data !== undefined && renderView(result.data)}
          </PageSectionContent>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default AccessRequestsPage;
