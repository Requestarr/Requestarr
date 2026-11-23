import React, { useCallback, useState } from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import useRequestarrApi from '../../useRequestarrApi';
import ConnectionCard, {
  AddConnectionCard,
  CardLabel,
} from '../ConnectionCard';
import EditSeriesFixModal from './EditSeriesFixModal';
import { SeriesFix } from './seriesFixTypes';
import styles from '../Connections.css';

function fixLabels(fix: SeriesFix): CardLabel[] {
  const labels: CardLabel[] = [
    fix.enabled
      ? { text: translate('SeriesFixOn'), kind: 'success' }
      : { text: translate('SeriesFixOff'), kind: 'disabled' },
  ];

  if (fix.shipped) {
    labels.push({ text: translate('SeriesFixShipped'), kind: 'disabled' });
  }

  return labels;
}

interface FixCardProps {
  fix: SeriesFix;
  onEdit: (fix: SeriesFix) => void;
}

function FixCard({ fix, onEdit }: FixCardProps) {
  const onPress = useCallback(() => onEdit(fix), [fix, onEdit]);

  return (
    <ConnectionCard
      name={fix.series}
      type={translate('SeriesFixTvdb', { id: fix.tvdbId })}
      detail={fix.reason ?? undefined}
      labels={fixLabels(fix)}
      onPress={onPress}
    />
  );
}

// Fixes for shows Sonarr grabs wrong, as cards like Sonarr's release profiles: one per show, by its TheTVDB ID, and
// the "+" card to add one. Each is built in Sonarr only when its show is requested. Sonarr only: Radarr has no release
// profiles.
function SeriesFixesSection() {
  const { data: fixes, reload } = useRequestarrApi<SeriesFix[]>('seriesfixes');
  const [editing, setEditing] = useState<SeriesFix | 'new'>();
  const onAdd = useCallback(() => setEditing('new'), []);
  const onClose = useCallback(() => setEditing(undefined), []);
  const onSaved = useCallback(() => {
    setEditing(undefined);
    reload();
  }, [reload]);

  return (
    <div id="series-fixes">
      <FieldSet legend={translate('SeriesFixes')}>
        <div className={styles.cards}>
          {fixes?.map((fix) => (
            <FixCard key={fix.id} fix={fix} onEdit={setEditing} />
          ))}
          <AddConnectionCard onPress={onAdd} />
        </div>
      </FieldSet>

      <EditSeriesFixModal
        isOpen={!!editing}
        fix={editing === 'new' ? undefined : editing}
        onClose={onClose}
        onSaved={onSaved}
      />
    </div>
  );
}

export default SeriesFixesSection;
