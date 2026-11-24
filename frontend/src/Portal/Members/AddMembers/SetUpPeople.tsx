import React, { useCallback, useMemo, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
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
import { OnboardSetup, Who } from './useStartRun';
import styles from './AddMembers.css';

// A week, as requests count a limit.
const DEFAULT_DAYS = 7;

// The most requests a limit allows, as the member card has it.
const MAX_LIMIT = 1000;

// What the admin changed of one type's access and limit.
type Picks = Partial<Omit<OnboardAccess, 'contentType'>>;

// One type for everyone picked: what the admin changed, the rest as the server gives a new member. Until the admin
// picks an access, it's the type's default access.
function accessFor(
  type: ContentType,
  picks: Picks,
  instances: PortalInstance[]
): OnboardAccess {
  return {
    contentType: type,
    state: picks.state ?? defaultAccess(type, instances),
    limit: picks.limit ?? null,
    days: picks.days ?? null,
  };
}

interface TypeFieldsProps {
  access: OnboardAccess;
  onChange: (type: ContentType, picks: Picks) => void;
}

// One type's access and limit for everyone picked; a limit of 0 keeps the server's.
function TypeFields({ access, onChange }: TypeFieldsProps) {
  const type = access.contentType;
  const onFieldChange = useCallback(
    ({ name, value }: { name: string; value: string | number }) => {
      if (name === 'state') {
        onChange(type, { state: value as AccessState });
      } else {
        onChange(type, { limit: Number(value) || null, days: DEFAULT_DAYS });
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
    <FormGroup>
      <FormLabel>{translate('PortalOnboardHistory')}</FormLabel>
      <FormInputGroup
        type={inputTypes.CHECK}
        name="postHistory"
        value={value}
        helpText={translate('PortalOnboardHistoryHelpText')}
        onChange={onInputChange}
      />
    </FormGroup>
  );
}

interface SetUpPeopleProps {
  people: Who[];
  isStarting: boolean;

  // Back to the list on the Onboard page; Cancel in the window the Members list opens.
  backLabel?: string;
  onBack: () => void;
  onConfirm: (setup: OnboardSetup) => void;
}

// Add member's second step: what everyone picked gets, once for all of them, starting from each media manager's
// default access for new members, whether their imported requests are posted, then the summary and Confirm.
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
  const [postHistory, setPostHistory] = useState(true);
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
    () => onConfirm({ access, postHistory }),
    [access, postHistory, onConfirm]
  );

  // Until the types and media managers arrive, each type would read Off: Servarr's loading indicator instead.
  if (!types || !instances) {
    return <LoadingIndicator />;
  }

  return (
    <>
      <ul className={styles.names}>
        {people.map((person) => (
          <li key={person.discordId}>{person.name}</li>
        ))}
      </ul>
      {access.map((one) => (
        <TypeFields key={one.contentType} access={one} onChange={onChange} />
      ))}
      {people.some((person) => person.importedRequests > 0) && (
        <HistoryField value={postHistory} onChange={setPostHistory} />
      )}
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
