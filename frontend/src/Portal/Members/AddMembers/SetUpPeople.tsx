import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import SpinnerButton from 'Components/Link/SpinnerButton';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import ModalBody from 'Components/Modal/ModalBody';
import ModalContent from 'Components/Modal/ModalContent';
import ModalFooter from 'Components/Modal/ModalFooter';
import ModalHeader from 'Components/Modal/ModalHeader';
import { inputTypes, kinds } from 'Helpers/Props';
import { AppSettings } from 'Settings/Requestarr/RequestarrSettingsTypes';
import useRequestarrApi from 'Settings/Requestarr/useRequestarrApi';
import translate from 'Utilities/String/translate';
import defaultAccess from '../../defaultAccess';
import { plainText } from '../../portalText';
import { ContentType, PortalInstance } from '../../PortalTypes';
import usePortal from '../../usePortal';
import { ActionOutcome } from '../../usePortalAction';
import TypeAccessFields, {
  DEFAULT_DAYS,
  TypeAccess,
} from '../TypeAccessFields';
import onboardTitle from './onboardTitle';
import { OnboardSetup, Who } from './useStartRun';
import styles from './AddMembers.css';

// What the admin changed, per type.
type Picks = Partial<Record<ContentType, Partial<TypeAccess>>>;

interface HistoryFieldProps {
  value: boolean;
  onChange: (value: boolean) => void;
}

// Whether the requests an import brought for them are posted in their channel.
function HistoryField({ value, onChange }: HistoryFieldProps) {
  const onInputChange = useCallback(
    (change: { value: boolean }) => onChange(change.value),
    [onChange]
  );

  return (
    <FieldSet legend={translate('PortalOnboardHistory')}>
      <FormGroup>
        <FormLabel>{translate('PortalOnboardPostHistory')}</FormLabel>
        <FormInputGroup
          type={inputTypes.CHECK}
          name="postHistory"
          value={value}
          helpText={translate('PortalOnboardHistoryHelpText')}
          onChange={onInputChange}
        />
      </FormGroup>
    </FieldSet>
  );
}

// Each type's access and limit for everyone picked: what the admin changed, until then the type's default access and
// no limit; and whether their history is posted, until the admin ticks or unticks it what the server's setting says.
function useSetup() {
  const types = usePortal<ContentType[]>('types');
  const instances = usePortal<PortalInstance[]>('instances');
  const settings = useRequestarrApi<AppSettings>('settings');
  const [picks, setPicks] = useState<Picks>({});
  const [pickedHistory, setPickedHistory] = useState<boolean>();
  const access = useMemo(
    () =>
      (types.data ?? []).map((type) => ({
        contentType: type,
        state: defaultAccess(type, instances.data ?? []),
        limit: 0,
        days: DEFAULT_DAYS,
        ...picks[type],
      })),
    [types.data, instances.data, picks]
  );
  const onAccessChange = useCallback(
    (type: ContentType, changed: Partial<TypeAccess>) =>
      setPicks((current) => ({
        ...current,
        [type]: { ...current[type], ...changed },
      })),
    []
  );

  return {
    // Until all three arrive, each field would read Off.
    isReady: !!types.data && !!instances.data && !!settings.data,
    hasError: types.hasError || instances.hasError || !!settings.error,
    access,
    postHistory:
      pickedHistory ?? settings.data?.values.onboardPostHistory === true,
    onAccessChange,
    onHistoryChange: setPickedHistory,
  };
}

interface SetUpPeopleProps {
  people: Who[];
  refused?: ActionOutcome;
  isStarting: boolean;
  onConfirm: (setup: OnboardSetup) => void;
  onClose: () => void;
}

// The Onboard window's first step: what everyone picked gets, once for all of them, and whether their imported
// requests are posted.
function SetUpPeople(props: SetUpPeopleProps) {
  const { people, refused, isStarting, onConfirm, onClose } = props;
  const setup = useSetup();
  const { isReady, access, postHistory } = setup;
  const onConfirmPress = useCallback(
    () => onConfirm({ access, postHistory }),
    [access, postHistory, onConfirm]
  );

  return (
    <ModalContent onModalClose={onClose}>
      <ModalHeader>{onboardTitle(people)}</ModalHeader>

      <ModalBody>
        {refused && (
          <Alert kind={kinds.DANGER}>{plainText(refused.message)}</Alert>
        )}

        {setup.hasError && (
          <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
        )}

        {!isReady && !setup.hasError && <LoadingIndicator />}

        {people.length > 1 && (
          <ul className={styles.names}>
            {people.map((person) => (
              <li key={person.discordId}>{person.name}</li>
            ))}
          </ul>
        )}

        {isReady &&
          access.map((one) => (
            <TypeAccessFields
              key={one.contentType}
              type={one.contentType}
              access={one}
              onChange={setup.onAccessChange}
            />
          ))}

        {isReady && people.some((person) => person.importedRequests > 0) && (
          <HistoryField value={postHistory} onChange={setup.onHistoryChange} />
        )}
      </ModalBody>

      <ModalFooter>
        <Button onPress={onClose}>{translate('Cancel')}</Button>

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isStarting}
          isDisabled={!isReady || isStarting}
          onPress={onConfirmPress}
        >
          {translate('PortalActionOnboard')}
        </SpinnerButton>
      </ModalFooter>
    </ModalContent>
  );
}

export default SetUpPeople;
