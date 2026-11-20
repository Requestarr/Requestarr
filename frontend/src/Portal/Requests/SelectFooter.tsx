import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import { PortalRequest } from '../PortalTypes';
import { ActionOutcome } from '../usePortalAction';
import ActionButton from './ActionButton';
import BulkActionModal from './BulkActionModal';
import runBulk, { BULK_PATHS } from './runBulk';
import styles from './SelectFooter.css';

const BULK_ACTIONS = Object.keys(BULK_PATHS);

interface SelectFooterProps {
  picked: PortalRequest[];
  onSelectAll: () => void;
  onClear: () => void;

  // After a bulk action: the list as it is now, nothing picked.
  onDone: () => void;
}

// Select mode's footer, as Sonarr's: how many are picked, Select all and Clear, and the actions every one of them
// offers now. Each asks first, listing the titles.
function SelectFooter({
  picked,
  onSelectAll,
  onClear,
  onDone,
}: SelectFooterProps) {
  const [asking, setAsking] = useState<string>();
  const [isBusy, setIsBusy] = useState(false);
  const [outcome, setOutcome] = useState<ActionOutcome>();

  const onClose = useCallback(() => setAsking(undefined), []);
  const onConfirm = useCallback(
    (body: object) => {
      setAsking(undefined);
      setIsBusy(true);
      runBulk(asking ?? '', picked, body).then((result) => {
        setIsBusy(false);
        setOutcome(result);
        onDone();
      });
    },
    [asking, picked, onDone]
  );

  return (
    <div className={styles.footer}>
      <ActionOutcomeAlert outcome={outcome} />
      <div className={styles.bar}>
        <span className={styles.count}>
          {translate('PortalSelectedCount', { count: picked.length })}
        </span>
        <Button onPress={onSelectAll}>{translate('PortalSelectAll')}</Button>
        <Button onPress={onClear}>{translate('PortalSelectNone')}</Button>
        <span className={styles.actions}>
          {BULK_ACTIONS.map((action) => (
            <ActionButton
              key={action}
              action={action}
              isBusy={isBusy}
              isDisabled={
                !picked.length ||
                !picked.every((request) => request.actions.includes(action))
              }
              onPress={setAsking}
            />
          ))}
        </span>
      </div>
      <BulkActionModal
        action={asking}
        requests={picked}
        onConfirm={onConfirm}
        onClose={onClose}
      />
    </div>
  );
}

export default SelectFooter;
