import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import columnsOf from '../Components/columnsOf';
import PortalList, { visibleCells } from '../Components/PortalList';
import PortalListToolbar from '../Components/PortalListToolbar';
import { PortalOutage, PortalOutageNow, PortalOutages } from '../PortalTypes';
import ActionButton from '../Requests/ActionButton';
import usePortal from '../usePortal';
import usePortalAction, { PortalAction } from '../usePortalAction';
import styles from '../Portal.css';

const nowColumns = columnsOf([
  'PortalInstance',
  'PortalDownSince',
  'PortalAnnounced',
  'PortalActions',
]);

function NowRow({
  outage,
  action,
}: {
  outage: PortalOutageNow;
  action: PortalAction;
}) {
  const onPress = useCallback(
    (name: string) => action.act(`instances/${outage.instanceId}/${name}`),
    [action, outage]
  );

  return (
    <TableRow>
      <TableRowCell>{outage.instance}</TableRowCell>
      <RelativeDateCell date={outage.since} />
      <TableRowCell>
        {outage.announced
          ? outage.announcedBy ?? translate('Yes')
          : translate('No')}
      </TableRowCell>
      <TableRowCell className={styles.actions}>
        <ActionButton action="check" isBusy={action.isBusy} onPress={onPress} />
        {outage.announceOffered && !outage.announced && (
          <ActionButton
            action="announce"
            isBusy={action.isBusy}
            onPress={onPress}
          />
        )}
      </TableRowCell>
    </TableRow>
  );
}

// A past outage, in the columns shown.
function historyRow(outage: PortalOutage, columns: Column[]) {
  return (
    <TableRow>
      {visibleCells(columns, {
        instance: <TableRowCell>{outage.instance}</TableRowCell>,
        started: <RelativeDateCell date={outage.started} />,
        ended: <RelativeDateCell date={outage.ended} />,
        announced: (
          <TableRowCell>
            {outage.announcedBy ??
              (outage.announced ? translate('Yes') : translate('No'))}
          </TableRowCell>
        ),
      })}
    </TableRow>
  );
}

interface OutagesViewProps {
  data: PortalOutages;

  // Goes up after each action, so the past outages are read again (an ongoing one is in them too).
  round: number;
  onDone: () => void;
}

function OutagesView({ data, round, onDone }: OutagesViewProps) {
  const action = usePortalAction(onDone);

  return (
    <>
      <FieldSet legend={translate('PortalDownNow')}>
        {data.now.length ? (
          <Table columns={nowColumns}>
            <TableBody>
              {data.now.map((outage) => (
                <NowRow
                  key={outage.instanceId}
                  outage={outage}
                  action={action}
                />
              ))}
            </TableBody>
          </Table>
        ) : (
          <div className={styles.empty}>{translate('PortalAllReachable')}</div>
        )}
      </FieldSet>

      <PortalList
        name="outageHistory"
        title={translate('PortalOutageHistory')}
        emptyText={translate('PortalNoOutages')}
        round={round}
        renderRow={historyRow}
      />
    </>
  );
}

// Sonarr and Radarr that can't be reached now (Check now, Announce to users, as the admin channel's alert has), and
// every outage long enough to alert the admins.
function OutagesPage() {
  const result = usePortal<PortalOutages>('outages?history=false');
  const [round, setRound] = useState(0);
  const { reload } = result;
  const onRefresh = useCallback(() => {
    reload();
    setRound((current) => current + 1);
  }, [reload]);
  const render = useCallback(
    (data: PortalOutages) => (
      <OutagesView data={data} round={round} onDone={onRefresh} />
    ),
    [round, onRefresh]
  );

  return (
    <PageContent title={translate('PortalOutages')}>
      <PortalListToolbar name="outageHistory" onRefreshPress={onRefresh} />

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
            {result.data !== undefined && render(result.data)}
          </PageSectionContent>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default OutagesPage;
