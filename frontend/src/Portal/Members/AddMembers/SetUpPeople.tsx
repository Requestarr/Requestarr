import React, { useCallback, useMemo, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../../accessLevelChoices';
import defaultAccess from '../../defaultAccess';
import { accessStateText, contentTypeText } from '../../portalText';
import {
  AccessState,
  ContentType,
  OnboardAccess,
  PortalInstance,
} from '../../PortalTypes';
import usePortal from '../../usePortal';
import { Who } from './useStartRun';
import styles from './AddMembers.css';

// A week, as requests count a limit.
const DEFAULT_DAYS = 7;

// The most requests a limit allows, as the member card has it.
const MAX_LIMIT = 1000;

// What the admin changed of one type's access, instance and limit.
type Picks = Partial<Omit<OnboardAccess, 'contentType'>>;

// One type for everyone picked: what the admin changed, the rest as the server gives a new member. Until the admin
// picks an access, it follows the instance: the default access of the one picked, or of the type's default.
function accessFor(
  type: ContentType,
  picks: Picks,
  instances: PortalInstance[]
): OnboardAccess {
  const instanceId = picks.instanceId ?? null;

  return {
    contentType: type,
    state: picks.state ?? defaultAccess(type, instances, instanceId),
    limit: picks.limit ?? null,
    days: picks.days ?? null,
    instanceId,
  };
}

interface TypeFieldsProps {
  access: OnboardAccess;
  instances: PortalInstance[];
  onChange: (type: ContentType, picks: Picks) => void;
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
  const type = access.contentType;
  const onFieldChange = useCallback(
    ({ name, value }: { name: string; value: string | number }) => {
      if (name === 'state') {
        onChange(type, { state: value as AccessState });
      } else if (name === 'limit') {
        onChange(type, { limit: Number(value) || null, days: DEFAULT_DAYS });
      } else {
        onChange(type, { instanceId: Number(value) || null });
      }
    },
    [type, onChange]
  );

  return (
    <FormGroup>
      <FormLabel>{contentTypeText(type)}</FormLabel>
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
          min={0}
          max={MAX_LIMIT}
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
      .filter((a) => a.state !== 'off')
      .map((a) => {
        const state = accessStateText(a.state).toLowerCase();

        return `${contentTypeText(a.contentType)} ${state}`;
      })
      .join(', '),
  });
}

interface SetUpPeopleProps {
  people: Who[];
  isStarting: boolean;

  // Back to the list on the Onboard page; Cancel in the window the Members list opens.
  backLabel?: string;
  onBack: () => void;
  onConfirm: (access: OnboardAccess[]) => void;
}

// Add member's second step: what everyone picked gets, once for all of them, starting from each media manager's
// default access for new members, then the summary and Confirm.
function SetUpPeople({
  people,
  isStarting,
  backLabel = translate('PortalBack'),
  onBack,
  onConfirm,
}: SetUpPeopleProps) {
  const { data: types } = usePortal<ContentType[]>('types');
  const { data: instances } = usePortal<PortalInstance[]>('instances');
  const [picks, setPicks] = useState<Partial<Record<ContentType, Picks>>>({});
  const access = useMemo(
    () =>
      (types ?? []).map((type) =>
        accessFor(type, picks[type] ?? {}, instances ?? [])
      ),
    [types, picks, instances]
  );
  const onChange = useCallback(
    (type: ContentType, changed: Picks) =>
      setPicks((current) => ({
        ...current,
        [type]: { ...current[type], ...changed },
      })),
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
            (instance) =>
              instance.contentType === one.contentType && instance.enabled
          )}
          onChange={onChange}
        />
      ))}
      <p className={styles.summary}>{summary(people.length, access)}</p>
      <div className={styles.buttons}>
        <Button onPress={onBack}>{backLabel}</Button>
        <Button
          kind={kinds.PRIMARY}
          isDisabled={isStarting || !instances}
          onPress={onConfirmPress}
        >
          {translate('PortalConfirm')}
        </Button>
      </div>
    </>
  );
}

export default SetUpPeople;
