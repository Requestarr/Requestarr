import React from 'react';
import DescriptionList from 'Components/DescriptionList/DescriptionList';
import DescriptionListItem from 'Components/DescriptionList/DescriptionListItem';
import Button from 'Components/Link/Button';
import Modal from 'Components/Modal/Modal';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import translate from 'Utilities/String/translate';
import { PortalEvent } from '../PortalTypes';
import useDateText from './useDateText';

interface EventDetailsModalProps {
  event: PortalEvent;
  isOpen: boolean;
  onModalClose: () => void;
}

// One entry of a timeline or history in full, as Sonarr main's Activity/History/Details/HistoryDetailsModal shows an
// event: what happened as the heading, then when, who and the whole detail.
function EventDetailsModal({
  event,
  isOpen,
  onModalClose,
}: EventDetailsModalProps) {
  const dateText = useDateText();

  return (
    <Modal isOpen={isOpen} onModalClose={onModalClose}>
      <ModalContent onModalClose={onModalClose}>
        <ModalHeader>{event.what}</ModalHeader>

        <ModalBody>
          <DescriptionList>
            <DescriptionListItem
              title={translate('PortalWhen')}
              data={dateText(event.at)}
            />
            <DescriptionListItem
              title={translate('PortalWho')}
              data={event.who}
            />
            {event.detail ? (
              <DescriptionListItem
                title={translate('PortalDetail')}
                data={event.detail}
              />
            ) : null}
          </DescriptionList>
        </ModalBody>

        <ModalFooter>
          <Button onPress={onModalClose}>{translate('Close')}</Button>
        </ModalFooter>
      </ModalContent>
    </Modal>
  );
}

export default EventDetailsModal;
