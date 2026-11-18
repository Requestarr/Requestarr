import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ActionModal from '../Components/ActionModal';
import parseSeasons from '../Components/parseSeasons';
import RemovalModal from '../Components/RemovalModal';
import TextPromptModal, { reasonPrompt } from '../Components/TextPromptModal';

export type RequestDialog = 'deny' | 'cancel' | 'seasons' | 'remove';

interface RequestDialogAnswers {
  onReason: (dialog: 'deny' | 'cancel', reason: string) => void;
  onSeasons: (seasons: number[], future: boolean) => void;
  onRemove: (seasons: number[], force: boolean) => void;
}

interface RequestActionDialogsProps extends RequestDialogAnswers {
  dialog?: RequestDialog;
  onClose: () => void;
}

const NO_SEASONS = { seasons: '', future: false };

// Add seasons' fields and their words, as Discord's picker has them: the seasons, and new ones as they air.
const SEASON_FIELDS: [keyof typeof NO_SEASONS, string][] = [
  ['seasons', 'PortalAddSeasonsSeasons'],
  ['future', 'PortalAddSeasonsFuture'],
];

function promptFor(dialog?: RequestDialog) {
  if (dialog !== 'deny' && dialog !== 'cancel') {
    return undefined;
  }

  return reasonPrompt(
    translate(dialog === 'deny' ? 'PortalActionDeny' : 'PortalActionCancel')
  );
}

// The choices a request's actions ask for, as Discord's forms do: a reason, seasons, what a removal covers.
function RequestActionDialogs(props: RequestActionDialogsProps) {
  const { dialog, onReason, onSeasons, onRemove, onClose } = props;
  const [seasons, setSeasons] = useState(NO_SEASONS);

  const onReasonConfirm = useCallback(
    (reason: string) => onReason(dialog as 'deny' | 'cancel', reason),
    [dialog, onReason]
  );
  const onSeasonsChange = useCallback(
    ({ name, value }: { name: string; value: string | boolean }) =>
      setSeasons((current) => ({ ...current, [name]: value })),
    []
  );
  // Seasons that don't read as numbers add nothing; the field says why.
  const onSeasonsConfirm = useCallback(() => {
    const numbers = parseSeasons(seasons.seasons);

    if (numbers) {
      onSeasons(numbers, seasons.future);
    }
  }, [seasons, onSeasons]);

  return (
    <>
      <TextPromptModal
        prompt={promptFor(dialog)}
        onConfirm={onReasonConfirm}
        onClose={onClose}
      />

      <ActionModal
        isOpen={dialog === 'seasons'}
        title={translate('PortalActionAddSeasons')}
        confirmLabel={translate('PortalActionAddSeasons')}
        onConfirm={onSeasonsConfirm}
        onClose={onClose}
      >
        {SEASON_FIELDS.map(([name, key]) => (
          <FormGroup key={name}>
            <FormLabel>{translate(key)}</FormLabel>
            <FormInputGroup
              type={name === 'seasons' ? inputTypes.TEXT : inputTypes.CHECK}
              name={name}
              value={seasons[name]}
              helpText={translate(`${key}HelpText`)}
              helpTextWarning={
                name === 'seasons' && !parseSeasons(seasons.seasons)
                  ? translate('PortalNotSeasons')
                  : undefined
              }
              onChange={onSeasonsChange}
            />
          </FormGroup>
        ))}
      </ActionModal>

      <RemovalModal
        isOpen={dialog === 'remove'}
        onStart={onRemove}
        onClose={onClose}
      />
    </>
  );
}

export default RequestActionDialogs;
