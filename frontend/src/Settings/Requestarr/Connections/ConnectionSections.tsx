import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import AddConnectionModal, { MEDIA_SERVERS } from './AddConnectionModal';
import { AddConnectionCard } from './ConnectionCard';
import styles from './Connections.css';

// Plex and Jellyfin: no media server can be added yet, so the "+" card's Add modal says so.
export function MediaServersSection() {
  const [isAdding, setIsAdding] = useState(false);
  const onAdd = useCallback(() => setIsAdding(true), []);
  const onAddClose = useCallback(() => setIsAdding(false), []);

  return (
    <div id="media-servers">
      <FieldSet legend={translate('ConnectionsMediaServers')}>
        <div className={styles.cards}>
          <AddConnectionCard onPress={onAdd} />
        </div>
      </FieldSet>

      <AddConnectionModal
        isOpen={isAdding}
        groups={MEDIA_SERVERS}
        onSelect={onAddClose}
        onClose={onAddClose}
      />
    </div>
  );
}
