import React from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';
import { SettingsSection } from './RequestarrSettingsTypes';
import SettingField from './SettingField';
import { SettingsForm } from './useSettingsForm';

interface SettingsSectionsProps {
  sections: SettingsSection[];
  form: SettingsForm;
  showAdvanced?: boolean;
}

// Each section's fields, the advanced ones only when they're asked for.
function SettingsSections({
  sections,
  form,
  showAdvanced = true,
}: SettingsSectionsProps) {
  return (
    <>
      {sections.map((section) => (
        <FieldSet key={section.legend} legend={translate(section.legend)}>
          {section.fields
            .filter(
              ([name]) => showAdvanced || !section.advanced?.includes(name)
            )
            .map(([name, input]) => (
              <SettingField key={name} name={name} input={input} form={form} />
            ))}
        </FieldSet>
      ))}
    </>
  );
}

export default SettingsSections;
