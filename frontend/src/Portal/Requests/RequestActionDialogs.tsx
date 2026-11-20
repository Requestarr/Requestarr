import React, { useCallback, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import ActionModal from '../Components/ActionModal';
import ForceRemovalConfirm from '../Components/ForceRemovalConfirm';
import parseSeasons from '../Components/parseSeasons';
import RemovalModal from '../Components/RemovalModal';
import TextPromptModal, { reasonPrompt } from '../Components/TextPromptModal';
import { actionHeading, actionLabel } from './ActionButton';

export type RequestDialog = 'deny' | 'cancel' | 'seasons' | 'remove' | 'force';

interface RequestDialogAnswers {
  onReason: (dialog: 'deny' | 'cancel', reason: string) => void;
  onSeasons: (seasons: number[], future: boolean) => void;
  onRemove: (seasons: number[], force: boolean) => void;
  onForce: () => void;
}

interface RequestActionDialogsProps extends RequestDialogAnswers {
  dialog?: RequestDialog;

  // The request's title, as the confirmations name it.
  title: string;

  // Sonarr and Radarr don't have it (Not in library): its removal has no seasons and deletes nothing.
  isRecordOnly: boolean;
  onClose: () => void;
}

const NO_SEASONS = { seasons: '', future: false };

// Add seasons' fields and their words, as Discord's picker has them: the seasons, and new ones as they air.
const SEASON_FIELDS: [keyof typeof NO_SEASONS, string][] = [
  ['seasons', 'PortalAddSeasonsSeasons'],
  ['future', 'PortalAddSeasonsFuture'],
];

function promptFor(title: string, dialog?: RequestDialog) {
  if (dialog !== 'deny' && dialog !== 'cancel') {
    return undefined;
  }

  return {
    ...reasonPrompt(actionLabel(dialog)),
    title: actionHeading(dialog, title),
  };
}

interface ConfirmDialogsProps {
  dialog?: RequestDialog;
  title: string;
  onForce: () => void;
  onClose: () => void;
}

// The action that only asks "are you sure?": Force removal.
function ConfirmDialogs(props: ConfirmDialogsProps) {
  const { dialog, title, onForce, onClose } = props;

  return dialog === 'force' ? (
    <ForceRemovalConfirm title={title} onConfirm={onForce} onCancel={onClose} />
  ) : null;
}

// The choices a request's actions ask for, as Discord's forms do: a reason, seasons, what a removal covers, and the
// confirmations.
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
        prompt={promptFor(props.title, dialog)}
        onConfirm={onReasonConfirm}
        onClose={onClose}
      />

      <ActionModal
        isOpen={dialog === 'seasons'}
        title={actionHeading('addSeasons', props.title)}
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
        title={props.title}
        isRecordOnly={props.isRecordOnly}
        onStart={onRemove}
        onClose={onClose}
      />

      <ConfirmDialogs
        dialog={dialog}
        title={props.title}
        onForce={props.onForce}
        onClose={onClose}
      />
    </>
  );
}

export default RequestActionDialogs;
