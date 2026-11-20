import React from 'react';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import SpinnerButton from 'Components/Link/SpinnerButton';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InstanceAnimeFields from './InstanceAnimeFields';
import InstanceField from './InstanceField';
import { InstanceForm } from './useInstanceForm';

const SONARR = 'Sonarr';
const SERIES_TYPES = ['standard', 'anime', 'daily'];

// HasTestButton: false where the modal's footer has Test instead.
interface InstanceFieldsProps {
  form: InstanceForm;
  isReadOnly: boolean;
  hasTestButton?: boolean;
}

// The choices for a quality profile or root folder: what Test found, or else the saved value on its own.
function options(found: string[] | undefined, saved: string | undefined) {
  const values = found ?? (saved ? [saved] : []);

  return values.map((value) => ({ key: value, value }));
}

// A Sonarr or Radarr's settings: how to reach it, then what its requests use once Test has listed the choices.
function InstanceFields({
  form,
  isReadOnly,
  hasTestButton = true,
}: InstanceFieldsProps) {
  const { values, test, problem, isBusy, apiKeySet, onTest } = form;
  const field = { form, isReadOnly };

  return (
    <>
      <FieldSet
        legend={translate('InstanceConnection', {
          type: values.implementation,
        })}
      >
        <InstanceField name="name" type={inputTypes.TEXT} {...field} />
        <InstanceField name="url" type={inputTypes.TEXT} {...field} />
        <InstanceField name="browserUrl" type={inputTypes.TEXT} {...field} />
        <InstanceField name="apiKey" type={inputTypes.PASSWORD} {...field} />

        {apiKeySet && (
          <Alert kind={kinds.INFO}>{translate('InstanceApiKeySaved')}</Alert>
        )}

        {hasTestButton && (
          <SpinnerButton
            isSpinning={isBusy}
            isDisabled={isReadOnly}
            onPress={onTest}
          >
            {translate('Test')}
          </SpinnerButton>
        )}

        {problem && <Alert kind={kinds.DANGER}>{problem}</Alert>}

        {test && !test.problem && (
          <Alert kind={kinds.SUCCESS}>{translate('InstanceTestPassed')}</Alert>
        )}
      </FieldSet>

      <FieldSet legend={translate('InstanceRequests')}>
        <InstanceField
          name="qualityProfile"
          type={inputTypes.SELECT}
          values={options(test?.qualityProfiles, values.qualityProfile)}
          {...field}
        />
        <InstanceField
          name="rootFolder"
          type={inputTypes.SELECT}
          values={options(test?.rootFolders, values.rootFolder)}
          {...field}
        />

        {values.implementation === SONARR && (
          <InstanceField
            name="seriesType"
            type={inputTypes.SELECT}
            values={options(SERIES_TYPES, undefined)}
            {...field}
          />
        )}

        <InstanceField name="isDefault" type={inputTypes.CHECK} {...field} />
        <InstanceField name="enabled" type={inputTypes.CHECK} {...field} />
        <InstanceField name="pollingOnly" type={inputTypes.CHECK} {...field} />
      </FieldSet>

      {values.implementation === SONARR && <InstanceAnimeFields {...field} />}
    </>
  );
}

export default InstanceFields;
