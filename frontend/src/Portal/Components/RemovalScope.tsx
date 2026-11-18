import React, { useCallback } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

export interface RemovalChoice {
  seasons: string;
  force: boolean;
}

// "1, 3": seasons 1 and 3; empty: the whole title.
export function seasonNumbers(text: string) {
  return text
    .split(',')
    .map((part) => parseInt(part.trim()))
    .filter((season) => !isNaN(season));
}

interface RemovalScopeProps {
  choice: RemovalChoice;
  onChange: (choice: RemovalChoice) => void;
}

// What a removal deletes (the whole title, or some seasons) and whether it skips asking: Start removal asks the
// requester or holds the vote, as in Discord; Force deletes now.
function RemovalScope({ choice, onChange }: RemovalScopeProps) {
  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: string | boolean }) => {
      onChange({ ...choice, [name]: value });
    },
    [choice, onChange]
  );

  return (
    <>
      <FormGroup>
        <FormLabel>{translate('PortalRemovalSeasons')}</FormLabel>
        <FormInputGroup
          type={inputTypes.TEXT}
          name="seasons"
          value={choice.seasons}
          helpText={translate('PortalRemovalSeasonsHelpText')}
          onChange={onInputChange}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('PortalRemovalForce')}</FormLabel>
        <FormInputGroup
          type={inputTypes.CHECK}
          name="force"
          value={choice.force}
          helpText={translate('PortalRemovalForceHelpText')}
          onChange={onInputChange}
        />
      </FormGroup>
    </>
  );
}

export default RemovalScope;
