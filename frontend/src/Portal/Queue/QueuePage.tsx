import React, { useCallback } from 'react';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import PortalPage from '../Components/PortalPage';
import { queueKindText } from '../portalText';
import { PortalQueueItem } from '../PortalTypes';
import ActionButton from '../Requests/ActionButton';
import usePortal from '../usePortal';
import usePortalAction, { PortalAction } from '../usePortalAction';
import styles from '../Portal.css';

const columns: Column[] = [
  { name: 'since', label: () => translate('PortalWaitingSince') },
  { name: 'kind', label: () => translate('PortalWaitingFor') },
  { name: 'title', label: () => translate('PortalTitle') },
  { name: 'who', label: () => translate('PortalWho') },
  { name: 'actions', label: () => translate('PortalActions') },
].map((column) => ({ ...column, isVisible: true }));

// Pages for the items that have no request: the person asking to join, the instance that's down.
const PAGES: Record<string, string> = {
  accessRequest: '/access-requests',
  instanceDown: '/outages',
};

// The item's title opens the page that acts on it: its request, or the access requests or outages; a member's name
// opens the member.
function TitleCell({ item }: { item: PortalQueueItem }) {
  const title = item.title ?? '';
  const page = item.requestId
    ? `/requests/${item.requestId}`
    : PAGES[item.kind];

  return (
    <TableRowCell>{page ? <Link to={page}>{title}</Link> : title}</TableRowCell>
  );
}

// A media server link request is decided right here, as its card in the admin channel is.
function LinkActions({
  item,
  action,
}: {
  item: PortalQueueItem;
  action: PortalAction;
}) {
  const onPress = useCallback(
    (name: string) => action.act(`media-links/${item.key.slice(1)}/${name}`),
    [action, item]
  );

  return (
    <TableRowCell className={styles.actions}>
      {item.kind === 'mediaServerLink' &&
        ['approve', 'deny'].map((name) => (
          <ActionButton
            key={name}
            action={name}
            isBusy={action.isBusy}
            onPress={onPress}
          />
        ))}
    </TableRowCell>
  );
}

function WhoCell({ item }: { item: PortalQueueItem }) {
  const who = item.who ?? '';

  return (
    <TableRowCell>
      {item.memberId ? (
        <Link to={`/members/${item.memberId}`}>{who}</Link>
      ) : (
        who
      )}
    </TableRowCell>
  );
}

function QueueView({
  items,
  onDone,
}: {
  items: PortalQueueItem[];
  onDone: () => void;
}) {
  const action = usePortalAction(onDone);

  if (!items.length) {
    return <div className={styles.empty}>{translate('PortalQueueEmpty')}</div>;
  }

  return (
    <>
      <ActionOutcomeAlert outcome={action.outcome} />
      <Table columns={columns}>
        <TableBody>
          {items.map((item) => (
            <TableRow key={item.key}>
              <RelativeDateCell date={item.since} />
              <TableRowCell>{queueKindText(item.kind)}</TableRowCell>
              <TitleCell item={item} />
              <WhoCell item={item} />
              <LinkActions item={item} action={action} />
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </>
  );
}

// What's waiting on an admin: the same items, in the same order, as the admin channel's queue, each opening the page
// that acts on it.
function QueuePage() {
  const result = usePortal<PortalQueueItem[]>('queue');
  const onActionDone = result.reload;
  const renderQueue = useCallback(
    (items: PortalQueueItem[]) => (
      <QueueView items={items} onDone={onActionDone} />
    ),
    [onActionDone]
  );

  return (
    <PortalPage title={translate('PortalQueue')} result={result}>
      {renderQueue}
    </PortalPage>
  );
}

export default QueuePage;
