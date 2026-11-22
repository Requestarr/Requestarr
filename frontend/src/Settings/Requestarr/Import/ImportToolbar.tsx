import React from 'react';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import PageToolbarSeparator from 'Components/Page/Toolbar/PageToolbarSeparator';
import Column from 'Components/Table/Column';
import TableOptionsModalWrapper from 'Components/Table/TableOptions/TableOptionsModalWrapper';
import { align, icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

interface ImportToolbarProps {
  columns: Column[];
  inviteUrl?: string;
  canImport: boolean;
  isImporting: boolean;
  onImportPress: () => void;
  onInvitePress: () => void;
  onTableOptionChange: (payload: unknown) => void;
}

// The page's toolbar, as Sonarr's Wanted, then Missing puts its actions on the left and Table Options on the right:
// Import writes what the list shows, and Invite bot adds the bot to the server the old channels are in, only to read.
// Both stay in reach above the list.
function ImportToolbar(props: ImportToolbarProps) {
  return (
    <PageToolbar>
      <PageToolbarSection>
        <PageToolbarButton
          label={translate('ImportRun')}
          iconName={icons.ADD}
          isSpinning={props.isImporting}
          isDisabled={!props.canImport}
          onPress={props.onImportPress}
        />

        {/* PageToolbarSection counts its children for the overflow menu, so no fragment around these two. */}
        {props.inviteUrl ? <PageToolbarSeparator /> : null}

        {props.inviteUrl ? (
          <PageToolbarButton
            label={translate('ImportInviteBot')}
            iconName={icons.EXTERNAL_LINK}
            onPress={props.onInvitePress}
          />
        ) : null}
      </PageToolbarSection>

      <PageToolbarSection alignContent={align.RIGHT} collapseButtons={false}>
        <TableOptionsModalWrapper
          columns={props.columns}
          onTableOptionChange={props.onTableOptionChange}
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

export default ImportToolbar;
