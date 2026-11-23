import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Button from 'Components/Link/Button';
import ConfirmModal from 'Components/Modal/ConfirmModal';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { callRequestarrApi } from '../../requestarrApi';
import { DiscordServer } from '../../RequestarrSettingsTypes';
import useApiAction from '../../useApiAction';
import styles from '../ConnectionModalFooter.css';

interface ImportOnlyContentProps {
  server: DiscordServer;
  onClose: () => void;
  onRemoved: () => void;
}

// A server the bot only reads: its way to the import, with this server's channels ready to tick, and removing the
// bot once the import is done (asked first).
function ImportOnlyContent({
  server,
  onClose,
  onRemoved,
}: ImportOnlyContentProps) {
  const [isAsking, setIsAsking] = useState(false);
  const remove = useApiAction();
  const { run } = remove;
  const name = server.name ?? server.id;
  const onAsk = useCallback(() => setIsAsking(true), []);
  const onCancel = useCallback(() => setIsAsking(false), []);
  const onConfirm = useCallback(() => {
    run(
      () => callRequestarrApi(`discord/servers/${server.id}/leave`, 'POST'),
      onRemoved
    );
  }, [run, server.id, onRemoved]);

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{name}</ModalHeader>

      <ModalBody>
        <p>{translate('DiscordImportOnlyOn')}</p>
        {remove.problem && <Alert kind={kinds.DANGER}>{remove.problem}</Alert>}
      </ModalBody>

      <ModalFooter>
        <Button
          className={styles.deleteButton}
          kind={kinds.DANGER}
          onPress={onAsk}
        >
          {translate('DiscordRemoveBot')}
        </Button>
        <Button onPress={onClose}>{translate('Close')}</Button>
        <Button
          kind={kinds.PRIMARY}
          to={`/settings/import?server=${server.id}`}
        >
          {translate('DiscordImportRequests')}
        </Button>
      </ModalFooter>

      <ConfirmModal
        isOpen={isAsking}
        kind={kinds.DANGER}
        title={translate('DiscordRemoveBot')}
        message={translate('DiscordRemoveBotConfirm', { server: name })}
        confirmLabel={translate('DiscordRemoveBot')}
        isSpinning={remove.isBusy}
        onConfirm={onConfirm}
        onCancel={onCancel}
      />
    </ModalContent>
  );
}

interface ImportOnlyServerModalProps
  extends Omit<ImportOnlyContentProps, 'server'> {
  server?: DiscordServer;
}

// The edit modal of a server the bot only reads, for an import from Requestrr.
function ImportOnlyServerModal({
  server,
  ...content
}: ImportOnlyServerModalProps) {
  return (
    <Modal isOpen={!!server} onModalClose={content.onClose}>
      {server && <ImportOnlyContent server={server} {...content} />}
    </Modal>
  );
}

export default ImportOnlyServerModal;
