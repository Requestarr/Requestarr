import React from 'react';
import Card from 'Components/Card';
import Icon from 'Components/Icon';
import Label, { LabelProps } from 'Components/Label';
import { icons } from 'Helpers/Props';
import styles from './ConnectionCard.css';

export interface CardLabel {
  text: string;
  kind: NonNullable<LabelProps['kind']>;
}

interface ConnectionCardProps {
  name: string;
  type: string;
  labels: CardLabel[];
  onPress: () => void;
}

// One thing Requestarr connects to, as Sonarr's and Radarr's settings show an indexer: its name, its type and small
// status labels. Pressing it opens its edit modal.
function ConnectionCard({ name, type, labels, onPress }: ConnectionCardProps) {
  return (
    <Card className={styles.connection} onPress={onPress}>
      <div className={styles.name}>{name}</div>
      <div className={styles.type}>{type}</div>

      <div className={styles.labels}>
        {labels.map(({ text, kind }) => (
          <Label key={text} kind={kind}>
            {text}
          </Label>
        ))}
      </div>
    </Card>
  );
}

// The "+" card at the end of a section's cards, which opens its Add modal.
export function AddConnectionCard({ onPress }: { onPress: () => void }) {
  return (
    <Card className={styles.addConnection} onPress={onPress}>
      <div className={styles.center}>
        <Icon name={icons.ADD} size={45} />
      </div>
    </Card>
  );
}

export default ConnectionCard;
