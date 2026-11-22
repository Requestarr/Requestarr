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
  canPreview: boolean;
  isReading: boolean;
  canImport: boolean;
  isImporting: boolean;
  onPreviewPress: () => void;
  onImportPress: () => void;
  onTableOptionChange: (payload: unknown) => void;
}

// The page's toolbar, as Sonarr's Wanted, then Missing puts its search actions on the left and Table Options on the
// right: Preview reads the channels, Import writes what the preview listed. Both stay in reach above the list.
function ImportToolbar(props: ImportToolbarProps) {
  return (
    <PageToolbar>
      <PageToolbarSection>
        <PageToolbarButton
          label={translate('ImportPreview')}
          iconName={icons.SEARCH}
          isSpinning={props.isReading}
          isDisabled={!props.canPreview}
          onPress={props.onPreviewPress}
        />

        <PageToolbarSeparator />

        <PageToolbarButton
          label={translate('ImportRun')}
          iconName={icons.ADD}
          isSpinning={props.isImporting}
          isDisabled={!props.canImport}
          onPress={props.onImportPress}
        />
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
