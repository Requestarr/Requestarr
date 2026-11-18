import React, { useCallback, useState } from 'react';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import RemovalStart from '../Components/RemovalStart';
import SearchField from '../Components/SearchField';
import useSearch from '../Components/useSearch';
import { seasonsText } from '../portalText';
import { PortalLibraryTitle } from '../PortalTypes';
import usePortalAction from '../usePortalAction';
import styles from '../Portal.css';

interface FoundProps {
  title: PortalLibraryTitle;
  onPick: (title: PortalLibraryTitle) => void;
}

function Found({ title, onPick }: FoundProps) {
  const onPress = useCallback(() => onPick(title), [title, onPick]);
  const about = [title.instance, title.requester, seasonsText(title.seasons)];

  return (
    <div className={styles.found}>
      <span>
        {title.title}{' '}
        <span className={styles.muted}>
          {about.filter(Boolean).join(' · ')}
        </span>
      </span>
      <Button onPress={onPress}>{translate('PortalPick')}</Button>
    </div>
  );
}

interface RemoveTitleModalProps {
  isOpen: boolean;
  onClose: () => void;
  onDone: () => void;
}

// Remove a title, as the admin panel's button: any title in a Sonarr or Radarr library, requested here or not.
function RemoveTitleModal({ isOpen, onClose, onDone }: RemoveTitleModalProps) {
  const action = usePortalAction(onDone);
  const search = useSearch<PortalLibraryTitle>('library');
  const [picked, setPicked] = useState<PortalLibraryTitle>();
  const onStart = useCallback(
    (seasons: number[], force: boolean) => {
      action.act(`library/${picked?.instanceId}/${picked?.itemId}/removal`, {
        seasons,
        force,
      });
    },
    [action, picked]
  );

  return (
    <Modal isOpen={isOpen} onModalClose={onClose}>
      <ModalContent onModalClose={onClose}>
        <ModalHeader>{translate('PortalRemoveTitle')}</ModalHeader>
        <ModalBody>
          <SearchField
            {...search}
            helpText={translate('PortalRemoveTitleHelpText')}
          />
          {!picked &&
            search.found?.map((title) => (
              <Found
                key={`${title.instanceId}-${title.itemId}`}
                title={title}
                onPick={setPicked}
              />
            ))}
          {picked && (
            <>
              <div className={styles.found}>{picked.title}</div>
              <RemovalStart onStart={onStart} />
            </>
          )}
          <ActionOutcomeAlert outcome={action.outcome} />
        </ModalBody>
      </ModalContent>
    </Modal>
  );
}

export default RemoveTitleModal;
