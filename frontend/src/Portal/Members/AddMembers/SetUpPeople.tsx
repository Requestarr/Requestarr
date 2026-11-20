import React, { useCallback, useMemo, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../../accessLevelChoices';
import { accessStateText, contentTypeText } from '../../portalText';
import {
  AccessState,
  ContentType,
  OnboardAccess,
  PortalInstance,
  PortalServerMember,
} from '../../PortalTypes';
import usePortal from '../../usePortal';
import styles from './AddMembers.css';

// The server's defaults, as onboarding from Discord gives them: films and shows need approval, the rest are off.
const DEFAULT_STATE: Partial<Record<ContentType, AccessState>> = {
  movie: 'needsApproval',
  tv: 'needsApproval',
};

// A week, as requests count a limit.
const DEFAULT_DAYS = 7;

function defaultsFor(type: ContentType): OnboardAccess {
  return {
    contentType: type,
    state: DEFAULT_STATE[type] ?? 'off',
    limit: null,
    days: null,
    instanceId: null,
  };
}

interface TypeFieldsProps {
  access: OnboardAccess;
  instances: PortalInstance[];
  onChange: (access: OnboardAccess) => void;
}

// One type's access, instance and limit for everyone picked; a limit of 0 and the default instance keep the server's.
function TypeFields({ access, instances, onChange }: TypeFieldsProps) {
  const choices = [
    { key: 0, value: translate('PortalDefaultInstance') },
    ...instances.map((instance) => ({
      key: instance.id,
      value: instance.name,
    })),
  ];
  const onFieldChange = useCallback(
    ({ name, value }: { name: string; value: string | number }) => {
      if (name === 'state') {
        onChange({ ...access, state: value as AccessState });
      } else if (name === 'limit') {
        onChange({
          ...access,
          limit: Number(value) || null,
          days: DEFAULT_DAYS,
        });
      } else {
        onChange({ ...access, instanceId: Number(value) || null });
      }
    },
    [access, onChange]
  );

  return (
    <FormGroup>
      <FormLabel>{contentTypeText(access.contentType)}</FormLabel>
      <div className={styles.typeFields}>
        <FormInputGroup
          type={inputTypes.SELECT}
          name="state"
          value={access.state}
          values={accessLevelChoices()}
          onChange={onFieldChange}
        />
        <FormInputGroup
          type={inputTypes.SELECT}
          name="instanceId"
          value={access.instanceId ?? 0}
          values={choices}
          onChange={onFieldChange}
        />
        <FormInputGroup
          type={inputTypes.NUMBER}
          name="limit"
          value={access.limit ?? 0}
          helpText={translate('PortalOnboardLimitHelpText')}
          onChange={onFieldChange}
        />
      </div>
    </FormGroup>
  );
}

// "Onboard 6 people: Movies needs approval, TV needs approval".
function summary(count: number, access: OnboardAccess[]) {
  return translate('PortalOnboardSummary', {
    count,
    access: access
      .map(
        (a) =>
          `${contentTypeText(a.contentType)} ${accessStateText(
            a.state
          ).toLowerCase()}`
      )
      .join(', '),
  });
}

interface SetUpPeopleProps {
  people: PortalServerMember[];
  onBack: () => void;
  onConfirm: (access: OnboardAccess[]) => void;
}

// Add member's second step: what everyone picked gets, once for all of them, starting from the server's defaults,
// then the summary and Confirm.
function SetUpPeople({ people, onBack, onConfirm }: SetUpPeopleProps) {
  const { data: types } = usePortal<ContentType[]>('types');
  const { data: instances } = usePortal<PortalInstance[]>('instances');
  const [changed, setChanged] = useState<
    Partial<Record<ContentType, OnboardAccess>>
  >({});
  const access = useMemo(
    () => (types ?? []).map((type) => changed[type] ?? defaultsFor(type)),
    [types, changed]
  );
  const onChange = useCallback(
    (one: OnboardAccess) =>
      setChanged((current) => ({ ...current, [one.contentType]: one })),
    []
  );
  const onConfirmPress = useCallback(
    () => onConfirm(access),
    [access, onConfirm]
  );

  return (
    <>
      <ul className={styles.names}>
        {people.map((person) => (
          <li key={person.discordId}>{person.name}</li>
        ))}
      </ul>
      {access.map((one) => (
        <TypeFields
          key={one.contentType}
          access={one}
          instances={(instances ?? []).filter(
            (instance) => instance.contentType === one.contentType
          )}
          onChange={onChange}
        />
      ))}
      <p className={styles.summary}>{summary(people.length, access)}</p>
      <div className={styles.buttons}>
        <Button onPress={onBack}>{translate('PortalBack')}</Button>
        <Button kind={kinds.PRIMARY} onPress={onConfirmPress}>
          {translate('PortalConfirm')}
        </Button>
      </div>
    </>
  );
}

export default SetUpPeople;
