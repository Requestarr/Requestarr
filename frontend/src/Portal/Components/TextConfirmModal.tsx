import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ConfirmModal from 'Components/Modal/ConfirmModal';
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

interface TextConfirmProps {
  prompt: TextPrompt;
  onConfirm: (text: string) => void;
  onClose: () => void;
}

// Servarr's ConfirmModal, its message a field for the words the action needs.
function TextConfirm({ prompt, onConfirm, onClose }: TextConfirmProps) {
  const [text, setText] = useState('');
  const onChange = useCallback(
    ({ value }: { value: string }) => setText(value),
    []
  );
  const onConfirmPress = useCallback(() => onConfirm(text), [text, onConfirm]);

  return (
    <ConfirmModal
      isOpen={true}
      kind={kinds.PRIMARY}
      title={prompt.title}
      message={
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
      }
      confirmLabel={prompt.confirmLabel}
      cancelLabel={translate('Cancel')}
      onConfirm={onConfirmPress}
      onCancel={onClose}
    />
  );
}

interface TextConfirmModalProps {
  prompt?: TextPrompt;
  onConfirm: (text: string) => void;
  onClose: () => void;
}

// Asks for a few words before an action (a reason for a denial or cancellation, a Plex username) in Servarr's
// ConfirmModal. Open while there's a prompt; each opening starts empty.
function TextConfirmModal({
  prompt,
  onConfirm,
  onClose,
}: TextConfirmModalProps) {
  if (!prompt) {
    return null;
  }

  return (
    <TextConfirm prompt={prompt} onConfirm={onConfirm} onClose={onClose} />
  );
}

export default TextConfirmModal;
