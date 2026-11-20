import React, { useCallback } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import parseSeasons from './parseSeasons';

export interface RemovalChoice {
  seasons: string;
  force: boolean;
}

interface RemovalScopeProps {
  choice: RemovalChoice;

  // False for a request Sonarr and Radarr don't have: the whole request goes, there are no seasons to pick.
  withSeasons?: boolean;
  onChange: (choice: RemovalChoice) => void;
}

// What a removal deletes (the whole title, or some seasons) and whether it skips asking: Start removal asks the
// requester or holds the vote, as in Discord; Force deletes now.
function RemovalScope({
  choice,
  withSeasons = true,
  onChange,
}: RemovalScopeProps) {
  const onInputChange = useCallback(
    ({ name, value }: { name: string; value: string | boolean }) => {
      onChange({ ...choice, [name]: value });
    },
    [choice, onChange]
  );

  return (
    <>
      {withSeasons && (
        <FormGroup>
          <FormLabel>{translate('PortalRemovalSeasons')}</FormLabel>
          <FormInputGroup
            type={inputTypes.TEXT}
            name="seasons"
            value={choice.seasons}
            helpText={translate('PortalRemovalSeasonsHelpText')}
            helpTextWarning={
              parseSeasons(choice.seasons)
                ? undefined
                : translate('PortalNotSeasons')
            }
            onChange={onInputChange}
          />
        </FormGroup>
      )}

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
