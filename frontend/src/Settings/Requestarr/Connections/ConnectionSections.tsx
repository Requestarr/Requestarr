import React, { ReactNode, useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import InstanceModal, { InstanceEdit } from '../InstanceModal';
import {
  AppSettings,
  Instance,
  SettingsSection,
} from '../RequestarrSettingsTypes';
import AddConnectionModal from './AddConnectionModal';
import ConnectionCard, { AddConnectionCard, CardLabel } from './ConnectionCard';
import { instanceLabels } from './connectionLabels';
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

// A section with the one connection Requestarr keeps in its settings (the Discord bot, The Movie Database, GitHub):
// a heading over its card, which opens its edit modal.
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

interface InstanceCardProps {
  instance: Instance;
  settings?: AppSettings;
  onOpen: (instance: Instance) => void;
}

function InstanceCard({ instance, settings, onOpen }: InstanceCardProps) {
  const onPress = useCallback(() => onOpen(instance), [instance, onOpen]);

  return (
    <ConnectionCard
      name={instance.settings.name}
      type={instance.settings.implementation}
      labels={instanceLabels(instance, settings)}
      onPress={onPress}
    />
  );
}

interface MediaManagersSectionProps {
  instances?: Instance[];
  settings?: AppSettings;
  onSaved: () => void;
}

// Sonarr and Radarr instances, then the "+" card that adds one through the Add modal.
export function MediaManagersSection({
  instances,
  settings,
  onSaved,
}: MediaManagersSectionProps) {
  const [edit, setEdit] = useState<InstanceEdit>();
  const [isAdding, setIsAdding] = useState(false);

  const onOpen = useCallback((instance: Instance) => {
    setEdit({ instance, implementation: instance.settings.implementation });
  }, []);

  const onAdd = useCallback(() => setIsAdding(true), []);
  const onAddClose = useCallback(() => setIsAdding(false), []);

  const onSelect = useCallback((implementation: string) => {
    setIsAdding(false);
    setEdit({ implementation });
  }, []);

  const onEditClose = useCallback(() => setEdit(undefined), []);

  const onEditSaved = useCallback(() => {
    setEdit(undefined);
    onSaved();
  }, [onSaved]);

  return (
    <div id="media-managers">
      <FieldSet legend={translate('ConnectionsMediaManagers')}>
        <div className={styles.cards}>
          {instances?.map((instance) => (
            <InstanceCard
              key={instance.id}
              instance={instance}
              settings={settings}
              onOpen={onOpen}
            />
          ))}

          <AddConnectionCard onPress={onAdd} />
        </div>
      </FieldSet>

      <AddConnectionModal
        isOpen={isAdding}
        onSelect={onSelect}
        onClose={onAddClose}
      />

      <InstanceModal
        edit={edit}
        isReadOnly={settings?.provisioned ?? true}
        onSaved={onEditSaved}
        onClose={onEditClose}
      />
    </div>
  );
}
