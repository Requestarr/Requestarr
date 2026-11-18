import React from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import readOnlyProps from './readOnlyProps';
import { AppSettings, SettingInput } from './RequestarrSettingsTypes';
import { SETTING_CHOICES } from './settingsPages';
import { SettingsForm } from './useSettingsForm';
import styles from './SettingField.css';

const INPUT_TYPES: Record<SettingInput, string> = {
  text: inputTypes.TEXT,
  number: inputTypes.NUMBER,
  check: inputTypes.CHECK,
  password: inputTypes.PASSWORD,
  select: inputTypes.SELECT,
};

// A select's choices, in the shape the web app's select input takes, and its locked look; other inputs have none.
function selectProps(name: string, input: SettingInput) {
  if (input !== 'select') {
    return {};
  }

  return {
    values: SETTING_CHOICES[name].map(([key, label]) => ({
      key,
      value: translate(label),
    })),
    disabledClassName: styles.lockedSelect,
  };
}

interface SettingFieldProps {
  name: string;
  input: SettingInput;
  form: SettingsForm;
}

// The translation key for a setting's label: Setting and its API name, capitalized (removalVoteDays:
// SettingRemovalVoteDays). Its help is the same key with HelpText after it.
function labelKey(name: string) {
  return `Setting${name[0].toUpperCase()}${name.slice(1)}`;
}

// The help under a field: where a provisioned setting is changed instead, and whether a secret is already saved.
function helpText(name: string, input: SettingInput, settings: AppSettings) {
  if (settings.provisioned) {
    return translate('SetByProvisioningHelpText');
  }

  const help = translate(`${labelKey(name)}HelpText`);

  return input === 'password' && settings.values[name]
    ? `${help} ${translate('SettingSecretSaved')}`
    : help;
}

// One of Requestarr's settings, by its name in the settings API: its label and help come from the translations.
function SettingField({ name, input, form }: SettingFieldProps) {
  const settings = form.settings as AppSettings;
  const saved = input === 'password' ? '' : settings.values[name];
  const value = name in form.pending ? form.pending[name] : saved;

  return (
    <FormGroup>
      <FormLabel>{translate(labelKey(name))}</FormLabel>

      <FormInputGroup
        type={INPUT_TYPES[input]}
        name={name}
        value={value}
        {...selectProps(name, input)}
        helpText={helpText(name, input, settings)}
        {...readOnlyProps(settings.provisioned)}
        onChange={form.onChange}
      />
    </FormGroup>
  );
}

export default SettingField;
