import React, { ReactNode, useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import { SettingsSection } from '../RequestarrSettingsTypes';
import AddConnectionModal, { MEDIA_SERVERS } from './AddConnectionModal';
import ConnectionCard, { AddConnectionCard, CardLabel } from './ConnectionCard';
import SettingsConnectionModal from './SettingsConnectionModal';
import styles from './Connections.css';

interface SettingsCardSectionProps {
  id: string;
  legend: string;
  name: string;
  type: string;
  labels: CardLabel[];
  sections: SettingsSection[];
  showAdvanced: boolean;
  children?: ReactNode;
  onSaved: () => void;
}

// A section with the one connection Requestarr keeps in its settings (the Discord bot): a heading over its card, which
// opens its edit modal.
export function SettingsCardSection({
  id,
  legend,
  name,
  type,
  labels,
  sections,
  showAdvanced,
  children,
  onSaved,
}: SettingsCardSectionProps) {
  const [isOpen, setIsOpen] = useState(false);
  const onOpen = useCallback(() => setIsOpen(true), []);
  const onClose = useCallback(() => setIsOpen(false), []);

  const onModalSaved = useCallback(() => {
    setIsOpen(false);
    onSaved();
  }, [onSaved]);

  return (
    <div id={id}>
      <FieldSet legend={legend}>
        <div className={styles.cards}>
          <ConnectionCard
            name={name}
            type={type}
            labels={labels}
            onPress={onOpen}
          />
        </div>
      </FieldSet>

      <SettingsConnectionModal
        isOpen={isOpen}
        title={translate('InstanceEditTitle', { name })}
        sections={sections}
        showAdvanced={showAdvanced}
        onSaved={onModalSaved}
        onClose={onClose}
      >
        {children}
      </SettingsConnectionModal>
    </div>
  );
}

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
