import React from 'react';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes } from 'Helpers/Props';
import { InputChanged } from 'typings/inputs';
import translate from 'Utilities/String/translate';

const sizeOptions = [
  {
    key: 'small',
    get value() {
      return translate('Small');
    },
  },
  {
    key: 'medium',
    get value() {
      return translate('Medium');
    },
  },
  {
    key: 'large',
    get value() {
      return translate('Large');
    },
  },
];

// One of the view's on and off options: its name in the store, its label and its help text.
export interface IndexOption {
  name: string;
  label: string;
  helpText?: string;
}

interface IndexOptionsModalProps {
  isOpen: boolean;
  title: string;
  sizeLabel: string;
  values: Record<string, unknown>;
  options: IndexOption[];
  onOptionChange: (change: InputChanged) => void;
  onModalClose: () => void;
}

// The Posters and Overview views' Options, as Sonarr main's SeriesIndexPosterOptionsModalContent and
// SeriesIndexOverviewOptionsModalContent lay them out: the poster size, then a box for each thing the view can show.
function IndexOptionsModal(props: IndexOptionsModalProps) {
  const { isOpen, title, values, options, onOptionChange, onModalClose } =
    props;

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{title}</ModalHeader>

        <ModalBody>
          <Form>
            <FormGroup>
              <FormLabel>{props.sizeLabel}</FormLabel>

              <FormInputGroup
                type={inputTypes.SELECT}
                name="size"
                value={values.size}
                values={sizeOptions}
                onChange={onOptionChange}
              />
            </FormGroup>

            {options.map(({ name, label, helpText }) => (
              <FormGroup key={name}>
                <FormLabel>{label}</FormLabel>

                <FormInputGroup
                  type={inputTypes.CHECK}
                  name={name}
                  value={values[name]}
                  helpText={helpText}
                  onChange={onOptionChange}
                />
              </FormGroup>
            ))}
          </Form>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default IndexOptionsModal;
