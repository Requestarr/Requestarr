import React from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import translate from 'Utilities/String/translate';
import readOnlyProps from './readOnlyProps';
import { InstanceForm } from './useInstanceForm';

interface InstanceFieldProps {
  name: string;
  type: string;
  form: InstanceForm;
  isReadOnly: boolean;
  values?: { key: string; value: string }[];
}

// One field of an instance, by its name in the settings API; its label and help come from the translations.
function InstanceField({
  name,
  type,
  form,
  isReadOnly,
  values,
}: InstanceFieldProps) {
  const key = `Backend${name[0].toUpperCase()}${name.slice(1)}`;

  return (
    <FormGroup>
      <FormLabel>{translate(key)}</FormLabel>

      <FormInputGroup
        type={type}
        name={name}
        value={form.values[name as keyof typeof form.values] ?? ''}
        values={values}
        helpText={translate(`${key}HelpText`)}
        {...readOnlyProps(isReadOnly)}
        onChange={form.onChange}
      />
    </FormGroup>
  );
}

export default InstanceField;
