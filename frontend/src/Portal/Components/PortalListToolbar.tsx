import React from 'react';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import { align, icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { usePortalListOptions } from './PortalList';

interface PortalListToolbarProps {
  // The list's section in the store.
  name: string;
  onRefreshPress: () => void;
}

// A list page's toolbar, as Servarr's System, then Events (System/Events/LogsTable): Refresh on the left, the list's
// Options on the right.
function PortalListToolbar({ name, onRefreshPress }: PortalListToolbarProps) {
  const { list, onTableOptionChange } = usePortalListOptions(name);

  return (
    <PageToolbar>
      <PageToolbarSection>
        <PageToolbarButton
          label={translate('Refresh')}
          iconName={icons.REFRESH}
          spinningName={icons.REFRESH}
          isSpinning={list.isFetching}
          onPress={onRefreshPress}
        />
      </PageToolbarSection>

      <PageToolbarSection alignContent={align.RIGHT} collapseButtons={false}>
        <TableOptionsModalWrapper
          columns={list.columns}
          pageSize={list.pageSize}
          onTableOptionChange={onTableOptionChange}
        >
          <PageToolbarButton
            label={translate('Options')}
            iconName={icons.TABLE}
          />
        </TableOptionsModalWrapper>
      </PageToolbarSection>
    </PageToolbar>
  );
}

export default PortalListToolbar;
