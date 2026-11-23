import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { kinds } from 'Helpers/Props';
import {
  fetchMediaManagers,
  fetchMediaManagerSchema,
  selectMediaManagerSchema,
} from 'Store/Actions/settingsActions';
import MediaManager from 'typings/MediaManager';
import sortByProp from 'Utilities/Array/sortByProp';
import translate from 'Utilities/String/translate';
import { AppSettings } from '../../RequestarrSettingsTypes';
import AddConnectionModal, { ChoiceGroup } from '../AddConnectionModal';
import ConnectionCard, { AddConnectionCard } from '../ConnectionCard';
import { mediaManagerLabels, TestOutcome } from '../connectionLabels';
import EditMediaManagerModal from './EditMediaManagerModal';
import { updatesText } from './MediaManagerExtras';
import styles from '../Connections.css';

interface MediaManagerCardProps {
  item: MediaManager;
  settings?: AppSettings;
  tested?: TestOutcome;
  onOpen: (id: number) => void;
}

function MediaManagerCard({
  item,
  settings,
  tested,
  onOpen,
}: MediaManagerCardProps) {
  const onPress = useCallback(() => onOpen(item.id), [item.id, onOpen]);

  return (
    <ConnectionCard
      name={item.name}
      type={item.implementationName}
      labels={mediaManagerLabels(item, settings, tested)}
      detail={
        item.enable && item.updates
          ? updatesText(item.updates, item.implementation)
          : undefined
      }
      onPress={onPress}
    />
  );
}

// The Add modal's choices: what the schema offers, Sonarr and Radarr, each with what it's for.
function useAddChoices(schema: MediaManager[]): ChoiceGroup[] {
  return useMemo(
    () => [
      {
        legend: 'ConnectionsMediaManagers',
        choices: schema.map(({ implementation }) => ({
          implementation,
          description: `Connection${implementation}Description`,
        })),
      },
    ],
    [schema]
  );
}

// Which instance the edit modal shows: a saved one by id, or the new one picked in the Add modal.
interface Editing {
  id?: number;
}

// Legend: the section's heading; setup's step has its own, so it shows none.
interface MediaManagersSectionProps {
  settings?: AppSettings;
  outcomes?: TestOutcome[];
  legend?: string;
}

// The Sonarr and Radarr instances, as Prowlarr's Applications settings: a card for each from the provider store, the
// "+" card that adds one from the schema through the Add modal, and the edit modal. Outcomes: what Test All
// Connections found, shown on the cards in place of the last check.
function MediaManagersSection({
  settings,
  outcomes,
  legend = translate('ConnectionsMediaManagers'),
}: MediaManagersSectionProps) {
  const dispatch = useDispatch();
  const section = useSelector(
    (state: AppState) => state.settings.mediaManagers
  );
  const [isAdding, setIsAdding] = useState(false);
  const [editing, setEditing] = useState<Editing>();
  const items = useMemo(
    () => [...section.items].sort(sortByProp('name')),
    [section.items]
  );
  const choices = useAddChoices(section.schema);
  const [deleteNote, setDeleteNote] = useState<string>();

  useEffect(() => {
    dispatch(fetchMediaManagers());
  }, [dispatch]);

  const onAdd = useCallback(() => {
    dispatch(fetchMediaManagerSchema());
    setIsAdding(true);
  }, [dispatch]);

  const onAddClose = useCallback(() => setIsAdding(false), []);

  const onSelect = useCallback(
    (implementation: string) => {
      dispatch(selectMediaManagerSchema({ implementation }));
      setIsAdding(false);
      setEditing({});
    },
    [dispatch]
  );

  const onOpen = useCallback((id: number) => setEditing({ id }), []);
  const onEditClose = useCallback(() => setEditing(undefined), []);

  return (
    <div id="media-managers">
      <FieldSet legend={legend}>
        {deleteNote && <Alert kind={kinds.WARNING}>{deleteNote}</Alert>}

        <PageSectionContent
          errorMessage={translate('ConnectionLoadError')}
          isFetching={section.isFetching && !section.isPopulated}
          isPopulated={section.isPopulated}
          error={section.error}
        >
          <div className={styles.cards}>
            {items.map((item) => (
              <MediaManagerCard
                key={item.id}
                item={item}
                settings={settings}
                tested={outcomes?.find((outcome) => outcome.id === item.id)}
                onOpen={onOpen}
              />
            ))}

            <AddConnectionCard onPress={onAdd} />
          </div>
        </PageSectionContent>
      </FieldSet>

      <AddConnectionModal
        isOpen={isAdding}
        isLoading={!section.isSchemaPopulated}
        groups={choices}
        onSelect={onSelect}
        onClose={onAddClose}
      />

      <EditMediaManagerModal
        isOpen={!!editing}
        id={editing?.id}
        isReadOnly={settings?.provisioned ?? true}
        onModalClose={onEditClose}
        onDeleted={setDeleteNote}
      />
    </div>
  );
}

export default MediaManagersSection;
