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

// A saved secret shows as the server sends it, dots and its last characters, so the admin can tell which is saved; a
// new one being typed over it is hidden as Servarr hides any password.
function inputType(input: SettingInput, isTyped: boolean) {
  return input === 'password' && !isTyped
    ? inputTypes.TEXT
    : INPUT_TYPES[input];
}

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

// The help under a field, or where a provisioned setting is changed instead.
function helpText(name: string, settings: AppSettings) {
  return settings.provisioned
    ? translate('SetByProvisioningHelpText')
    : translate(`${labelKey(name)}HelpText`);
}

// One of Requestarr's settings, by its name in the settings API: its label and help come from the translations.
function SettingField({ name, input, form }: SettingFieldProps) {
  const settings = form.settings as AppSettings;
  const isTyped = name in form.pending;
  const value = isTyped ? form.pending[name] : settings.values[name];

  return (
    <FormGroup>
      <FormLabel>{translate(labelKey(name))}</FormLabel>

      <FormInputGroup
        type={inputType(input, isTyped)}
        name={name}
        value={value}
        {...selectProps(name, input)}
        helpText={helpText(name, settings)}
        {...readOnlyProps(settings.provisioned)}
        onChange={form.onChange}
      />
    </FormGroup>
  );
}

export default SettingField;
