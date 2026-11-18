import React from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import { SettingsSection } from './RequestarrSettingsTypes';
import SettingField from './SettingField';
import { SettingsForm } from './useSettingsForm';

interface SettingsSectionsProps {
  sections: SettingsSection[];
  form: SettingsForm;
}

function SettingsSections({ sections, form }: SettingsSectionsProps) {
  return (
    <>
      {sections.map((section) => (
        <FieldSet key={section.legend} legend={translate(section.legend)}>
          {section.fields.map(([name, input]) => (
            <SettingField key={name} name={name} input={input} form={form} />
          ))}
        </FieldSet>
      ))}
    </>
  );
}

export default SettingsSections;
