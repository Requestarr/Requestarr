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
import ReconnectServer from './ReconnectServer';
import styles from '../ConnectionModalFooter.css';

interface RemoveServerProps {
  server: DiscordServer;
  isBusy: boolean;
  onRemove: () => void;
}

// Taking a server the bot only reads off the list (asked first): the bot leaves it, or Requestarr forgets one that
// removed the bot already.
function RemoveServer({ server, isBusy, onRemove }: RemoveServerProps) {
  const [isAsking, setIsAsking] = useState(false);
  const onAsk = useCallback(() => setIsAsking(true), []);
  const onCancel = useCallback(() => setIsAsking(false), []);
  const label = translate(server.joined ? 'DiscordRemoveBot' : 'Delete');

  return (
    <>
      <Button
        className={styles.deleteButton}
        kind={kinds.DANGER}
        onPress={onAsk}
      >
        {label}
      </Button>

      <ConfirmModal
        isOpen={isAsking}
        kind={kinds.DANGER}
        title={label}
        message={translate(
          server.joined ? 'DiscordRemoveBotConfirm' : 'DiscordForgetConfirm',
          { server: server.name ?? server.id }
        )}
        confirmLabel={label}
        isSpinning={isBusy}
        onConfirm={onRemove}
        onCancel={onCancel}
      />
    </>
  );
}

interface ServerContentProps {
  server: DiscordServer;
  channelsLabel: string;
  onClose: () => void;
  onRemoved: () => void;
}

function ServerContent(props: ServerContentProps) {
  const { server, channelsLabel, onClose, onRemoved } = props;
  const remove = useApiAction();
  const { run } = remove;
  const onRemove = useCallback(
    () =>
      run(
        () => callRequestarrApi(`discord/servers/${server.id}/leave`, 'POST'),
        onRemoved
      ),
    [run, server.id, onRemoved]
  );

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>
        {server.name ?? translate('DiscordServerUnknown')}
      </ModalHeader>

      <ModalBody>
        <p>
          {translate(
            server.isOurs ? 'DiscordImportOnlyOff' : 'DiscordImportOnlyOn'
          )}
        </p>
        {!server.joined && (
          <ReconnectServer server={server} onJoined={onClose} />
        )}
        {remove.problem && <Alert kind={kinds.DANGER}>{remove.problem}</Alert>}
      </ModalBody>

      <ModalFooter>
        {!server.isOurs && (
          <RemoveServer
            server={server}
            isBusy={remove.isBusy}
            onRemove={onRemove}
          />
        )}
        <Button onPress={onClose}>{translate('Close')}</Button>
        {server.joined && (
          <Button
            kind={kinds.PRIMARY}
            to={`/settings/import?server=${server.id}`}
            onPress={onClose}
          >
            {channelsLabel}
          </Button>
        )}
      </ModalFooter>
    </ModalContent>
  );
}

interface ServerModalProps extends Omit<ServerContentProps, 'server'> {
  server?: DiscordServer;
}

// A server's window, from its card: its channels on the Import page, and taking the bot out of it or adding it back.
function ServerModal({ server, ...content }: ServerModalProps) {
  return (
    <Modal isOpen={!!server} onModalClose={content.onClose}>
      {server && <ServerContent server={server} {...content} />}
    </Modal>
  );
}

export default ServerModal;
