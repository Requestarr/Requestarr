import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useDeletesFiles, { forceRemovalMessage } from '../useDeletesFiles';
import parseSeasons from './parseSeasons';
import RemovalScope, { RemovalChoice } from './RemovalScope';

interface RemovalStartProps {
  onStart: (seasons: number[], force: boolean) => void;

  // Opened from Force removal: Force starts ticked.
  isForce?: boolean;
}

// What a removal covers, then Start removal. Seasons that don't read as numbers start nothing, and Force asks once
// more before deleting, as Discord's "Confirm delete" does. With deleting turned off, it says nothing is deleted.
function RemovalStart({ onStart, isForce = false }: RemovalStartProps) {
  const deletesFiles = useDeletesFiles();
  const [choice, setChoice] = useState<RemovalChoice>({
    seasons: '',
    force: isForce,
  });
  const [isConfirming, setIsConfirming] = useState(false);
  const seasons = parseSeasons(choice.seasons);

  const onStartPress = useCallback(() => {
    if (seasons && choice.force) {
      setIsConfirming(true);
    } else if (seasons) {
      onStart(seasons, false);
    }
  }, [seasons, choice, onStart]);
  const onForce = useCallback(() => {
    setIsConfirming(false);
    onStart(seasons ?? [], true);
  }, [seasons, onStart]);
  const onCancel = useCallback(() => setIsConfirming(false), []);

  return (
    <>
      {deletesFiles === false && (
        <Alert kind={kinds.INFO}>{translate('PortalDeletingOffNote')}</Alert>
      )}
      <RemovalScope choice={choice} onChange={setChoice} />
      <Button kind={kinds.DANGER} isDisabled={!seasons} onPress={onStartPress}>
        {translate('PortalActionRemove')}
      </Button>
      <ConfirmModal
        isOpen={isConfirming}
        kind={kinds.DANGER}
        title={translate('PortalRemovalForce')}
        message={forceRemovalMessage(deletesFiles)}
        confirmLabel={translate('PortalRemovalForce')}
        onConfirm={onForce}
        onCancel={onCancel}
      />
    </>
  );
}

export default RemovalStart;
