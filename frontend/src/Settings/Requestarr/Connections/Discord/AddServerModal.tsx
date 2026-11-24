import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import readOnlyProps from '../../readOnlyProps';
import { SetupStatus } from '../../RequestarrSettingsTypes';
import useBotJoin from './useBotJoin';
import WaitingForBot from './WaitingForBot';

interface AddServerContentProps {
  setup: SetupStatus;
  importOnly: boolean;
  onClose: () => void;
}

// Off: the full invite, for the server Requestarr runs in, offered only while it has none. On: the invite that only
// reads (view channels, read history), for an import from Requestrr. Either way the window closes once the bot joins,
// and the new server's card is there.
function AddServerContent({
  setup,
  importOnly,
  onClose,
}: AddServerContentProps) {
  // Requestarr's server, while the bot is in it: then only a server to read can be added.
  const ours = setup.servers.find(
    (server) => server.isOurs && server.reachable
  );
  const [isImportOnly, setIsImportOnly] = useState(importOnly || !!ours);
  const [isWaiting, setIsWaiting] = useState(false);
  const url = isImportOnly ? setup.readOnlyInviteUrl : setup.inviteUrl;
  const onChange = useCallback(
    ({ value }: { value: boolean }) => setIsImportOnly(value),
    []
  );
  const onInvite = useCallback(() => {
    window.open(url, '_blank', 'noopener');
    setIsWaiting(true);
  }, [url]);

  useBotJoin(setup.servers, isWaiting, onClose);

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{translate('DiscordAddServer')}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{translate('DiscordImportOnly')}</FormLabel>
          <div>
            <FormInputGroup
              type={inputTypes.CHECK}
              name="importOnly"
              value={isImportOnly}
              {...readOnlyProps(!!ours)}
              onChange={onChange}
            />
            <FormInputHelpText text={translate('DiscordImportOnlyOn')} />
            <FormInputHelpText text={translate('DiscordImportOnlyOff')} />
          </div>
        </FormGroup>

        {ours && (
          <Alert kind={kinds.INFO}>
            {translate('DiscordAddServerHasOurs', { server: ours.name ?? '' })}
          </Alert>
        )}

        <p>{translate('DiscordInviteHelpText')}</p>
        {isWaiting && <WaitingForBot />}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onClose}>{translate('Close')}</Button>
        <Button kind={kinds.PRIMARY} isDisabled={!url} onPress={onInvite}>
          {translate('DiscordOpenInvite')}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

interface AddServerModalProps extends AddServerContentProps {
  isOpen: boolean;
}

// The servers' "+" card: one way to add the bot to a server, wherever the admin starts (Connections, the setup, or
// the Import page, which opens it with Import only on).
function AddServerModal({ isOpen, ...content }: AddServerModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={content.onClose}>
      {isOpen && <AddServerContent {...content} />}
    </Modal>
  );
}

export default AddServerModal;
