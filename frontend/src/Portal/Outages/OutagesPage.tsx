import React, { useCallback } from 'react';
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
import { PortalOutageNow, PortalOutages } from '../PortalTypes';
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
const historyColumns = columnsOf([
  'PortalInstance',
  'PortalStarted',
  'PortalEnded',
  'PortalAnnounced',
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

function OutagesView({
  data,
  onDone,
}: {
  data: PortalOutages;
  onDone: () => void;
}) {
  const action = usePortalAction(onDone);

  return (
    <>
      <ActionOutcomeAlert outcome={action.outcome} />

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

      <FieldSet legend={translate('PortalOutageHistory')}>
        {data.history.length ? (
          <Table columns={historyColumns}>
            <TableBody>
              {data.history.map((outage) => (
                <TableRow key={`${outage.instance}-${outage.started}`}>
                  <TableRowCell>{outage.instance}</TableRowCell>
                  <RelativeDateCell date={outage.started} />
                  <RelativeDateCell date={outage.ended} />
                  <TableRowCell>
                    {outage.announcedBy ??
                      (outage.announced ? translate('Yes') : translate('No'))}
                  </TableRowCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        ) : (
          <div className={styles.empty}>{translate('PortalNoOutages')}</div>
        )}
      </FieldSet>
    </>
  );
}

// Sonarr and Radarr that can't be reached now (Check now, Announce to users, as the admin channel's alert has), and
// every outage long enough to alert the admins.
function OutagesPage() {
  const result = usePortal<PortalOutages>('outages');
  const onActionDone = result.reload;
  const render = useCallback(
    (data: PortalOutages) => <OutagesView data={data} onDone={onActionDone} />,
    [onActionDone]
  );

  return (
    <PortalPage title={translate('PortalOutages')} result={result}>
      {render}
    </PortalPage>
  );
}

export default OutagesPage;
