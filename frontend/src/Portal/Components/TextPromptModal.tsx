import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

export interface TextPrompt {
  title: string;
  label: string;
  helpText: string;
  confirmLabel: string;
}

// The reason an action asks for (a denial, a cancellation), titled and confirmed with the action's words.
export function reasonPrompt(actionWords: string): TextPrompt {
  return {
    title: actionWords,
    label: translate('PortalReason'),
    helpText: translate('PortalReasonHelpText'),
    confirmLabel: actionWords,
  };
}

interface TextPromptContentProps {
  prompt: TextPrompt;
  onConfirm: (text: string) => void;
  onClose: () => void;
}

function TextPromptContent({
  prompt,
  onConfirm,
  onClose,
}: TextPromptContentProps) {
  const [text, setText] = useState('');

  const onChange = useCallback(({ value }: { value: string }) => {
    setText(value);
  }, []);

  const onConfirmPress = useCallback(() => onConfirm(text), [text, onConfirm]);

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{prompt.title}</ModalHeader>

      <ModalBody>
        <FormGroup>
          <FormLabel>{prompt.label}</FormLabel>
          <FormInputGroup
            type={inputTypes.TEXT}
            name="text"
            value={text}
            helpText={prompt.helpText}
            onChange={onChange}
          />
        </FormGroup>
      </ModalBody>

      <ModalFooter>
        <Button onPress={onClose}>{translate('Cancel')}</Button>
        <Button kind={kinds.PRIMARY} onPress={onConfirmPress}>
          {prompt.confirmLabel}
        </Button>
      </ModalFooter>
    </ModalContent>
  );
}

interface TextPromptModalProps {
  prompt?: TextPrompt;
  onConfirm: (text: string) => void;
  onClose: () => void;
}

// Asks for a few words before an action: a reason for a denial or cancellation, a Plex username. Open while there's a
// prompt; each opening starts empty.
function TextPromptModal({ prompt, onConfirm, onClose }: TextPromptModalProps) {
  return (
    <Modal isOpen={!!prompt} onModalClose={onClose}>
      {prompt && (
        <TextPromptContent
          prompt={prompt}
          onConfirm={onConfirm}
          onClose={onClose}
        />
      )}
    </Modal>
  );
}

export default TextPromptModal;
