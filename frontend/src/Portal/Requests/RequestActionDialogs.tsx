import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ActionModal from '../Components/ActionModal';
import RemovalScope, {
  RemovalChoice,
  seasonNumbers,
} from '../Components/RemovalScope';
import TextPromptModal, { reasonPrompt } from '../Components/TextPromptModal';

export type RequestDialog = 'deny' | 'cancel' | 'seasons' | 'remove';

interface RequestDialogAnswers {
  onReason: (dialog: 'deny' | 'cancel', reason: string) => void;
  onSeasons: (seasons: number[], all: boolean, future: boolean) => void;
  onRemove: (seasons: number[], force: boolean) => void;
}

interface RequestActionDialogsProps extends RequestDialogAnswers {
  dialog?: RequestDialog;
  onClose: () => void;
}

const NO_REMOVAL: RemovalChoice = { seasons: '', force: false };
const NO_SEASONS = { seasons: '', all: false, future: false };

// Add seasons' fields and their words: the seasons, or all of them; and new ones as they air.
const SEASON_FIELDS: [keyof typeof NO_SEASONS, string][] = [
  ['seasons', 'PortalAddSeasonsSeasons'],
  ['all', 'PortalAddSeasonsAll'],
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
  const [removal, setRemoval] = useState(NO_REMOVAL);
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
  const onSeasonsConfirm = useCallback(
    () =>
      onSeasons(seasonNumbers(seasons.seasons), seasons.all, seasons.future),
    [seasons, onSeasons]
  );
  const onRemoveConfirm = useCallback(
    () => onRemove(seasonNumbers(removal.seasons), removal.force),
    [removal, onRemove]
  );

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
              onChange={onSeasonsChange}
            />
          </FormGroup>
        ))}
      </ActionModal>

      <ActionModal
        isOpen={dialog === 'remove'}
        title={translate('PortalActionRemove')}
        confirmLabel={translate('PortalActionRemove')}
        onConfirm={onRemoveConfirm}
        onClose={onClose}
      >
        <RemovalScope choice={removal} onChange={setRemoval} />
      </ActionModal>
    </>
  );
}

export default RequestActionDialogs;
