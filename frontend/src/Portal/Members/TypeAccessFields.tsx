import React, { useCallback } from 'react';
import FieldSet from 'Components/FieldSet';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import accessLevelChoices from '../accessLevelChoices';
import { contentTypeText } from '../portalText';
import { AccessState, ContentType } from '../PortalTypes';

// Without a limit of their own, a member's days read a week, as requests count it.
export const DEFAULT_DAYS = 7;

// The most a limit allows, as the member card in Discord has it (UserManagementService).
const MAX_LIMIT = 1000;
const MAX_DAYS = 365;

// A member's access to one type, and their limit: so many requests in so many days, 0 for none.
export interface TypeAccess {
  state: AccessState;
  limit: number;
  days: number;
}

interface TypeAccessFieldsProps {
  type: ContentType;
  access: TypeAccess;
  onChange: (type: ContentType, changed: Partial<TypeAccess>) => void;
}

// One type's access and limit, in the windows that onboard people and change a member's access.
function TypeAccessFields({ type, access, onChange }: TypeAccessFieldsProps) {
  const onStateChange = useCallback(
    ({ value }: { value: AccessState }) => onChange(type, { state: value }),
    [type, onChange]
  );
  const onNumberChange = useCallback(
    ({ name, value }: { name: string; value: number | null }) =>
      onChange(type, { [name]: value ?? 0 }),
    [type, onChange]
  );

  return (
    <FieldSet legend={contentTypeText(type)}>
      <FormGroup>
        <FormLabel>{translate('PortalEditState')}</FormLabel>
        <FormInputGroup
          type={inputTypes.SELECT}
          name="state"
          value={access.state}
          values={accessLevelChoices()}
          onChange={onStateChange}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('PortalEditLimit')}</FormLabel>
        <FormInputGroup
          type={inputTypes.NUMBER}
          name="limit"
          value={access.limit}
          min={0}
          max={MAX_LIMIT}
          onChange={onNumberChange}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('PortalEditDays')}</FormLabel>
        <FormInputGroup
          type={inputTypes.NUMBER}
          name="days"
          value={access.days}
          min={1}
          max={MAX_DAYS}
          onChange={onNumberChange}
        />
      </FormGroup>
    </FieldSet>
  );
}

export default TypeAccessFields;
