import React from 'react';
import Link from 'Components/Link/Link';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import PortalPage from '../Components/PortalPage';
import { queueKindText } from '../portalText';
import { PortalQueueItem } from '../PortalTypes';
import usePortal from '../usePortal';
import styles from '../Portal.css';

const columns: Column[] = [
  { name: 'since', label: () => translate('PortalWaitingSince') },
  { name: 'kind', label: () => translate('PortalWaitingFor') },
  { name: 'title', label: () => translate('PortalTitle') },
  { name: 'who', label: () => translate('PortalWho') },
].map((column) => ({ ...column, isVisible: true }));

// The item's title opens its request when it has one; a member's name opens the member.
function TitleCell({ item }: { item: PortalQueueItem }) {
  const title = item.title ?? '';

  return (
    <TableRowCell>
      {item.requestId ? (
        <Link to={`/requests/${item.requestId}`}>{title}</Link>
      ) : (
        title
      )}
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

function renderQueue(items: PortalQueueItem[]) {
  if (!items.length) {
    return <div className={styles.empty}>{translate('PortalQueueEmpty')}</div>;
  }

  return (
    <Table columns={columns}>
      <TableBody>
        {items.map((item) => (
          <TableRow key={item.key}>
            <RelativeDateCell date={item.since} />
            <TableRowCell>{queueKindText(item.kind)}</TableRowCell>
            <TitleCell item={item} />
            <WhoCell item={item} />
          </TableRow>
        ))}
      </TableBody>
    </Table>
  );
}

// What's waiting on an admin: the same items, in the same order, as the admin channel's queue.
function QueuePage() {
  const result = usePortal<PortalQueueItem[]>('queue');

  return (
    <PortalPage title={translate('PortalQueue')} result={result}>
      {renderQueue}
    </PortalPage>
  );
}

export default QueuePage;
