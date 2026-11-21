import { IconDefinition } from '@fortawesome/fontawesome-svg-core';
import React, { ReactNode } from 'react';
import Icon from 'Components/Icon';
import Label, { LabelProps } from 'Components/Label';
import { sizes } from 'Helpers/Props';
import styles from './HeaderLabel.css';

interface HeaderLabelProps {
  iconName: IconDefinition;
  title?: string;
  kind?: LabelProps['kind'];
  children: ReactNode;
}

// One of a details page header's chips, as Sonarr main's SeriesDetails draws its path, network and status: a large
// label with an icon and its words.
function HeaderLabel({ iconName, title, kind, children }: HeaderLabelProps) {
  return (
    <Label
      className={styles.detailsLabel}
      title={title}
      kind={kind}
      size={sizes.LARGE}
    >
      <div>
        <Icon name={iconName} size={17} />
        <span className={styles.text}>{children}</span>
      </div>
    </Label>
  );
}

export default HeaderLabel;
