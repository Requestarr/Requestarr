import React from 'react';
import PageToolbar from 'Components/Page/Toolbar/PageToolbar';
import PageToolbarButton from 'Components/Page/Toolbar/PageToolbarButton';
import PageToolbarSection from 'Components/Page/Toolbar/PageToolbarSection';

interface PortalToolbarProps {
  label: string;
  iconName: object;
  onPress: () => void;
}

// A list page's one action, in the toolbar where Servarr's pages put theirs.
function PortalToolbar({ label, iconName, onPress }: PortalToolbarProps) {
  return (
    <PageToolbar>
      <PageToolbarSection>
        <PageToolbarButton
          label={label}
          iconName={iconName}
          onPress={onPress}
        />
      </PageToolbarSection>
    </PageToolbar>
  );
}

export default PortalToolbar;
