import React from 'react';
import FieldSet from 'Components/FieldSet';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import InstanceField from './InstanceField';
import { InstanceForm } from './useInstanceForm';

const PRIORITIES = ['dub', 'sub', 'first'].map((value) => ({
  key: value,
  value,
}));

interface InstanceAnimeFieldsProps {
  form: InstanceForm;
  isReadOnly: boolean;
}

// Sonarr only: whether anime asks sub or dub, which wins on this instance, and the release terms for each (empty for
// Requestarr's defaults).
function InstanceAnimeFields({ form, isReadOnly }: InstanceAnimeFieldsProps) {
  const field = { form, isReadOnly };

  return (
    <FieldSet legend={translate('InstanceAnime')}>
      <InstanceField name="animeEnabled" type={inputTypes.CHECK} {...field} />
      <InstanceField
        name="animePriority"
        type={inputTypes.SELECT}
        values={PRIORITIES}
        {...field}
      />
      <InstanceField name="dubRequired" type={inputTypes.TEXT} {...field} />
      <InstanceField name="dubIgnored" type={inputTypes.TEXT} {...field} />
      <InstanceField name="subRequired" type={inputTypes.TEXT} {...field} />
      <InstanceField name="subIgnored" type={inputTypes.TEXT} {...field} />
    </FieldSet>
  );
}

export default InstanceAnimeFields;
