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
} from '../../PortalTypes';
import usePortal from '../../usePortal';
import { Who } from './useStartRun';
import styles from './AddMembers.css';

// The server's defaults, as onboarding from Discord gives them: films and shows need approval, the rest are off.
const DEFAULT_STATE: Partial<Record<ContentType, AccessState>> = {
  movie: 'needsApproval',
  tv: 'needsApproval',
};

// A week, as requests count a limit.
const DEFAULT_DAYS = 7;

// The most requests a limit allows, as the member card has it.
const MAX_LIMIT = 1000;

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

// Add member's second step: what everyone picked gets, once for all of them, starting from the server's defaults,
// then the summary and Confirm.
function SetUpPeople({
  people,
  isStarting,
  backLabel = translate('PortalBack'),
  onBack,
  onConfirm,
}: SetUpPeopleProps) {
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
          isDisabled={isStarting}
          onPress={onConfirmPress}
        >
          {translate('PortalConfirm')}
        </Button>
      </div>
    </>
  );
}

export default SetUpPeople;
