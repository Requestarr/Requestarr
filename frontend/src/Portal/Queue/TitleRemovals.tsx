import React from 'react';
import FieldSet from 'Components/FieldSet';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import columnsOf from '../Components/columnsOf';
import RemovalActions from '../Components/RemovalActions';
import { removalStatusText } from '../portalText';
import { PortalTitleRemoval } from '../PortalTypes';
import usePortal from '../usePortal';
import usePortalAction from '../usePortalAction';
import styles from '../Portal.css';

const columns = columnsOf([
  'PortalTitle',
  'PortalStatus',
  'PortalStarted',
  'PortalStartedBy',
  'PortalKeeps',
  'PortalActions',
]);

// Open removals of titles nobody requested through Requestarr: they have no request page, so they're forced or
// dropped here, as their own feed cards in Discord are.
function TitleRemovals() {
  const { data: removals, reload } =
    usePortal<PortalTitleRemoval[]>('title-removals');
  const action = usePortalAction(reload);

  if (!removals?.length) {
    return null;
  }

  return (
    <FieldSet legend={translate('PortalTitleRemovals')}>
      <Table columns={columns}>
        <TableBody>
          {removals.map(({ title, removal }) => (
            <TableRow key={removal.id}>
              <TableRowCell>{title}</TableRowCell>
              <TableRowCell>{removalStatusText(removal.status)}</TableRowCell>
              <RelativeDateCell date={removal.created} />
              <TableRowCell>{removal.startedBy ?? ''}</TableRowCell>
              <TableRowCell>{removal.keeps.join(', ')}</TableRowCell>
              <TableRowCell className={styles.actions}>
                <RemovalActions
                  removalId={removal.id}
                  action={action}
                  byHand={removal.status === 'deleteByHand'}
                  isRecordOnly={removal.recordOnly}
                />
              </TableRowCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </FieldSet>
  );
}

export default TitleRemovals;
