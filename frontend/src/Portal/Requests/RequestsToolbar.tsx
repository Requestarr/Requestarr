import React from 'react';
import Menu from 'Components/Menu/Menu';
import MenuContent from 'Components/Menu/MenuContent';
import SelectedMenuItem from 'Components/Menu/SelectedMenuItem';
import ToolbarMenuButton from 'Components/Menu/ToolbarMenuButton';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';
import { align, icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { RequestsSort } from '../Components/RequestsTable';
import { REQUEST_VIEWS, RequestView } from './requestView';

// The columns the Overview and Posters views sort by, as the Table view's headers do.
const SORTS: [string, string][] = [
  ['title', 'PortalTitle'],
  ['type', 'PortalType'],
  ['status', 'PortalStatus'],
  ['requester', 'PortalRequester'],
  ['followers', 'PortalFollowers'],
  ['instance', 'PortalInstance'],
  ['requested', 'PortalRequested'],
];

const VIEW_LABELS: Record<RequestView, string> = {
  overview: 'PortalViewOverview',
  posters: 'PortalViewPosters',
  table: 'PortalViewTable',
};

interface RequestsToolbarProps {
  view: RequestView;
  sort: RequestsSort;
  isSelecting: boolean;
  onViewChange: (view: RequestView) => void;
  onSelectPress: () => void;
}

// Select on the left (select mode, as Sonarr's Select Series); on the right, the view (Overview, Posters, Table) and
// the sort, as Sonarr's and Radarr's lists have them.
function RequestsToolbar({
  view,
  sort,
  isSelecting,
  onViewChange,
  onSelectPress,
}: RequestsToolbarProps) {
  return (
    <PageToolbar>
      <PageToolbarSection>
        <PageToolbarButton
          label={translate(
            isSelecting ? 'PortalStopSelecting' : 'PortalSelect'
          )}
          iconName={isSelecting ? icons.SQUARE : icons.CHECK_SQUARE}
          onPress={onSelectPress}
        />
      </PageToolbarSection>

      <PageToolbarSection alignContent={align.RIGHT} collapseButtons={false}>
        <Menu alignMenu={align.RIGHT}>
          <ToolbarMenuButton
            iconName={icons.VIEW}
            text={translate('PortalView')}
          />
          <MenuContent>
            {REQUEST_VIEWS.map((name) => (
              <SelectedMenuItem
                key={name}
                name={name}
                isSelected={view === name}
                onPress={onViewChange}
              >
                {translate(VIEW_LABELS[name])}
              </SelectedMenuItem>
            ))}
          </MenuContent>
        </Menu>

        <Menu alignMenu={align.RIGHT}>
          <ToolbarMenuButton iconName={icons.SORT} text={translate('Sort')} />
          <MenuContent>
            {SORTS.map(([name, label]) => (
              <SelectedMenuItem
                key={name}
                name={name}
                isSelected={sort.sortKey === name}
                onPress={sort.onSortPress}
              >
                {translate(label)}
              </SelectedMenuItem>
            ))}
          </MenuContent>
        </Menu>
      </PageToolbarSection>
    </PageToolbar>
  );
}

export default RequestsToolbar;
