import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { callRequestarrApi } from '../../requestarrApi';
import useApiAction from '../../useApiAction';
import ConnectionModalFooter from '../ConnectionModalFooter';
import { NEW_FIX, SeriesFix, SeriesFixInput } from './seriesFixTypes';

// The fields in Sonarr's release profile order: the show, then its terms, then why and whether it's on.
const FIELDS: [keyof SeriesFixInput, string][] = [
  ['tvdbId', inputTypes.NUMBER],
  ['series', inputTypes.TEXT],
  ['mustContain', inputTypes.TEXT_TAG],
  ['mustNotContain', inputTypes.TEXT_TAG],
  ['reason', inputTypes.TEXT],
  ['enabled', inputTypes.CHECK],
];

const LABELS: Record<string, string> = {
  tvdbId: 'SeriesFixTvdbId',
  series: 'SeriesFixSeries',
  mustContain: 'SeriesFixMustContain',
  mustNotContain: 'SeriesFixMustNotContain',
  reason: 'SeriesFixReason',
  enabled: 'SeriesFixEnabled',
};

interface EditContentProps {
  fix?: SeriesFix;
  onClose: () => void;
  onSaved: () => void;
}

// One fix's form, built like Sonarr's Edit Release Profile: terms as tags, Delete on the left for a fix that isn't
// shipped (a shipped one can only be turned off), Cancel and Save.
function EditSeriesFixContent({ fix, onClose, onSaved }: EditContentProps) {
  const [values, setValues] = useState<SeriesFixInput>(fix ?? NEW_FIX);
  const save = useApiAction();
  const { run } = save;
  const onChange = useCallback(
    ({ name, value }: { name: string; value: unknown }) =>
      setValues((current) => ({ ...current, [name]: value })),
    []
  );
  const onSave = useCallback(() => {
    const path = fix ? `seriesfixes/${fix.id}` : 'seriesfixes';
    run(() => callRequestarrApi(path, fix ? 'PUT' : 'POST', values), onSaved);
  }, [run, fix, values, onSaved]);
  const onDelete = useCallback(() => {
    run(() => callRequestarrApi(`seriesfixes/${fix?.id}`, 'DELETE'), onSaved);
  }, [run, fix, onSaved]);

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>
        {translate(fix ? 'SeriesFixEdit' : 'SeriesFixAdd')}
      </ModalHeader>

      <ModalBody>
        <Alert kind={kinds.INFO}>{translate('SeriesFixHelp')}</Alert>
        <Form>
          {FIELDS.map(([name, type]) => (
            <FormGroup key={name}>
              <FormLabel>{translate(LABELS[name])}</FormLabel>
              <FormInputGroup
                type={type}
                name={name}
                value={values[name]}
                helpText={translate(`${LABELS[name]}Help`)}
                onChange={onChange}
              />
            </FormGroup>
          ))}
        </Form>
        {save.problem && <Alert kind={kinds.DANGER}>{save.problem}</Alert>}
      </ModalBody>

      <ConnectionModalFooter
        isReadOnly={false}
        isSaving={save.isBusy}
        canSave={true}
        onDelete={fix && !fix.shipped ? onDelete : undefined}
        onCancel={onClose}
        onSave={onSave}
      />
    </ModalContent>
  );
}

interface EditSeriesFixModalProps extends EditContentProps {
  isOpen: boolean;
}

function EditSeriesFixModal({ isOpen, ...content }: EditSeriesFixModalProps) {
  return (
    <Modal isOpen={isOpen} onModalClose={content.onClose}>
      {isOpen && <EditSeriesFixContent {...content} />}
    </Modal>
  );
}

export default EditSeriesFixModal;
