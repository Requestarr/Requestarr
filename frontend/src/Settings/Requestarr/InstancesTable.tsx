import React, { useCallback } from 'react';
import Link from 'Components/Link/Link';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import Column from 'Components/Table/Column';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import translate from 'Utilities/String/translate';
import { Instance } from './RequestarrSettingsTypes';

const columns: Column[] = [
  { name: 'name', label: () => translate('BackendName') },
  { name: 'implementation', label: () => translate('Type') },
  { name: 'url', label: () => translate('BackendUrl') },
  { name: 'isDefault', label: () => translate('BackendIsDefault') },
  { name: 'status', label: () => translate('Status') },
].map((column) => ({ ...column, isVisible: true }));

// Turned off, connected, or why the last check failed.
function statusText(instance: Instance) {
  if (!instance.settings.enabled) {
    return translate('InstanceTurnedOff');
  }

  return instance.connected
    ? translate('InstanceConnected')
    : instance.problem ?? translate('InstanceNotChecked');
}

interface InstanceRowProps {
  instance: Instance;
  onOpen: (instance: Instance) => void;
}

function InstanceRow({ instance, onOpen }: InstanceRowProps) {
  const { settings } = instance;
  const onNamePress = useCallback(() => onOpen(instance), [instance, onOpen]);

  return (
    <TableRow>
      <TableRowCell>
        <Link onPress={onNamePress}>{settings.name}</Link>
      </TableRowCell>
      <TableRowCell>{settings.implementation}</TableRowCell>
      <TableRowCell>{settings.url}</TableRowCell>
      <TableRowCell>{settings.isDefault ? translate('Yes') : ''}</TableRowCell>
      <TableRowCell>{statusText(instance)}</TableRowCell>
    </TableRow>
  );
}

interface InstancesTableProps {
  instances: Instance[];
  onOpen: (instance: Instance) => void;
}

function InstancesTable({ instances, onOpen }: InstancesTableProps) {
  if (!instances.length) {
    return <div>{translate('InstanceNone')}</div>;
  }

  return (
    <Table columns={columns}>
      <TableBody>
        {instances.map((instance) => (
          <InstanceRow key={instance.id} instance={instance} onOpen={onOpen} />
        ))}
      </TableBody>
    </Table>
  );
}

export default InstancesTable;
