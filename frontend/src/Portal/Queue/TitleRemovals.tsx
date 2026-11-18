import React from 'react';
import FieldSet from 'Components/FieldSet';
import RelativeDateCell from 'Components/Table/Cells/RelativeDateCell';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import RemovalActions from '../Components/RemovalActions';
import { removalStatusText } from '../portalText';
import { PortalTitleRemoval } from '../PortalTypes';
import usePortal from '../usePortal';
import usePortalAction from '../usePortalAction';
import styles from '../Portal.css';

const columns: Column[] = [
  'PortalTitle',
  'PortalStatus',
  'PortalStarted',
  'PortalStartedBy',
  'PortalKeeps',
  'PortalActions',
].map((label) => ({
  name: label,
  label: () => translate(label),
  isVisible: true,
}));

// Open removals of titles nobody requested through Requestarr: they have no request page, so they're forced or
// dropped here, as their own feed cards in Discord are.
function TitleRemovals() {
  const { data: removals, reload } =
    usePortal<PortalTitleRemoval[]>('title-removals');
  const action = usePortalAction(reload);

  // The last one forced or dropped leaves its answer.
  if (!removals?.length) {
    return <ActionOutcomeAlert outcome={action.outcome} />;
  }

  return (
    <FieldSet legend={translate('PortalTitleRemovals')}>
      <ActionOutcomeAlert outcome={action.outcome} />
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
                <RemovalActions removalId={removal.id} action={action} />
              </TableRowCell>
            </TableRow>
          ))}
        </TableBody>
      </Table>
    </FieldSet>
  );
}

export default TitleRemovals;
