import React, { useCallback, useState } from 'react';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import ActionOutcomeAlert from '../Components/ActionOutcomeAlert';
import RemovalStart from '../Components/RemovalStart';
import { seasonsText } from '../portalText';
import { PortalLibraryTitle } from '../PortalTypes';
import { actionHeading } from '../Requests/ActionButton';
import usePortalAction from '../usePortalAction';
import styles from '../Portal.css';

// No page to show again after it: the outcome is said in the dialog.
function nothingToReload() {
  return undefined;
}

interface LibraryRemovalProps {
  title: PortalLibraryTitle;
  isForce: boolean;
  onClose: () => void;
}

function LibraryRemovalContent({
  title,
  isForce,
  onClose,
}: LibraryRemovalProps) {
  const { act, outcome } = usePortalAction(nothingToReload);
  const onStart = useCallback(
    (seasons: number[], force: boolean) =>
      act(`library/${title.instanceId}/${title.itemId}/removal`, {
        seasons,
        force,
      }),
    [act, title]
  );
  const about = [title.instance, seasonsText(title.seasons)];

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>
        {actionHeading(isForce ? 'forceRemoval' : 'remove', title.label)}
      </ModalHeader>
      <ModalBody>
        <p className={styles.muted}>{about.filter(Boolean).join(' · ')}</p>
        {!outcome?.isDone && (
          <RemovalStart isForce={isForce} onStart={onStart} />
        )}
        <ActionOutcomeAlert outcome={outcome} />
      </ModalBody>
    </ModalContent>
  );
}

interface LibraryRemovalModalProps {
  title?: PortalLibraryTitle;
  isForce: boolean;
  onClose: () => void;
}

// Start removal or Force removal on a title nobody requested here, from its header search result: what it covers,
// then the removal, as the request pages start one.
function LibraryRemovalModal({
  title,
  isForce,
  onClose,
}: LibraryRemovalModalProps) {
  return (
    <Modal isOpen={!!title} onModalClose={onClose}>
      {title && (
        <LibraryRemovalContent
          title={title}
          isForce={isForce}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

// The removal a search result started, and its dialog. onStarted: what the search does as the dialog opens (close).
export function useLibraryRemoval(onStarted: () => void) {
  const [removing, setRemoving] = useState<{
    title: PortalLibraryTitle;
    isForce: boolean;
  }>();
  const onRemove = useCallback(
    (title: PortalLibraryTitle, isForce: boolean) => {
      setRemoving({ title, isForce });
      onStarted();
    },
    [onStarted]
  );
  const onClose = useCallback(() => setRemoving(undefined), []);
  const modal = (
    <LibraryRemovalModal
      title={removing?.title}
      isForce={removing?.isForce ?? false}
      onClose={onClose}
    />
  );

  return { onRemove, modal };
}
