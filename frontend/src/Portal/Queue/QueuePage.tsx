import React, { useCallback, useState } from 'react';
import Link from 'Components/Link/Link';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import Person from '../Components/Person';
import PortalList, { visibleCells } from '../Components/PortalList';
import PortalListToolbar from '../Components/PortalListToolbar';
import RemovalActions from '../Components/RemovalActions';
import { queueKindText } from '../portalText';
import { PortalQueueItem } from '../PortalTypes';
import ActionButton from '../Requests/ActionButton';
import usePortalAction, { PortalAction } from '../usePortalAction';
import TitleRemovals from './TitleRemovals';
import styles from '../Portal.css';

// Pages for the items that have no request: the person asking to join, the instance that's down, and the connections
// where an instance is given the purpose nobody has.
const PAGES: Record<string, string> = {
  accessRequest: '/access-requests',
  instanceDown: '/outages',
  noAllOther: '/settings/connections',
};

// The item's title opens the page that acts on it: its request, or the access requests, outages or connections; a
// member's name opens the member.
function TitleCell({ item }: { item: PortalQueueItem }) {
  const title = item.title ?? '';
  const page = item.requestId
    ? `/requests/${item.requestId}`
    : PAGES[item.kind];

  return (
    <TableRowCell>{page ? <Link to={page}>{title}</Link> : title}</TableRowCell>
  );
}

function WhoCell({ item }: { item: PortalQueueItem }) {
  const who = item.who ?? '';

  return (
    <TableRowCell>
      {item.memberId ? (
        <Link to={`/members/${item.memberId}`}>
          <Person
            name={who}
            handle={item.whoHandle}
            avatar={item.whoAvatar}
            size="small"
          />
        </Link>
      ) : (
        who
      )}
    </TableRowCell>
  );
}

interface ItemActionsProps {
  item: PortalQueueItem;
  action: PortalAction;
}

// The actions an item has right here, as its card in the admin channel does: a media server link request is decided,
// and a removal of a title nobody requested (it has no request page) is forced or dropped.
function ItemActions({ item, action }: ItemActionsProps) {
  const id = item.key.slice(1);
  const onPress = useCallback(
    (name: string) => action.act(`media-links/${id}/${name}`),
    [action, id]
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
      {(item.kind === 'removal' || item.kind === 'deleteByHand') &&
        !item.requestId && (
          <RemovalActions
            removalId={id}
            action={action}
            byHand={item.kind === 'deleteByHand'}
            isRecordOnly={item.recordOnly}
          />
        )}
    </TableRowCell>
  );
}

// What's waiting on an admin, a page at a time: the same items, in the same order, as the admin channel's queue, each
// opening the page that acts on it; after an action the page shown is read again.
function QueuePage() {
  const [round, setRound] = useState(0);
  const onDone = useCallback(() => setRound((current) => current + 1), []);
  const action = usePortalAction(onDone);
  const itemRow = useCallback(
    (item: PortalQueueItem, columns: Column[]) => (
      <TableRow>
        {visibleCells(columns, {
          since: <RelativeDateCell date={item.since} />,
          kind: <TableRowCell>{queueKindText(item.kind)}</TableRowCell>,
          title: <TitleCell item={item} />,
          who: <WhoCell item={item} />,
          actions: <ItemActions item={item} action={action} />,
        })}
      </TableRow>
    ),
    [action]
  );

  return (
    <PageContent title={translate('PortalQueue')}>
      <PortalListToolbar name="queue" onRefreshPress={onDone} />

      <PageContentBody>
        <PortalList
          name="queue"
          emptyText={translate('PortalQueueEmpty')}
          round={round}
          renderRow={itemRow}
        />
        <TitleRemovals round={round} />
      </PageContentBody>
    </PageContent>
  );
}

export default QueuePage;
