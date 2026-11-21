import React, { ComponentType, useCallback, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import { inputTypes } from 'Helpers/Props';
import {
  setMediaManagerFieldValue,
  setMediaManagerValue,
} from 'Store/Actions/settingsActions';
import createProviderSettingsSelector from 'Store/Selectors/createProviderSettingsSelector';
import { ProviderField } from 'typings/MediaManager';
import translate from 'Utilities/String/translate';
import readOnlyProps from '../../readOnlyProps';

// The schema's sections (MediaManagerActions on the server): Sonarr's own fields and the anime ones.
const SONARR_SECTION = 'sonarr';
const ANIME_SECTION = 'anime';
const SONARR = 'Sonarr';

// The media managers' provider section in the store (Store/Actions/Settings/mediaManagers.js).
export const MEDIA_MANAGERS_SECTION = 'settings.mediaManagers';

interface SettingChange {
  name: string;
  value: unknown;
}

// A setting as Servarr's selectSettings gives it: its value, with what validation found.
interface Setting<T> {
  value: T;
  errors: object[];
  warnings: object[];
}

export interface MediaManagerSettings {
  isFetching: boolean;
  error?: object;
  isSaving: boolean;
  saveError?: object;
  isTesting: boolean;
  validationErrors: object[];
  validationWarnings: object[];
  item: {
    id?: Setting<number>;
    name: Setting<string>;
    enable: Setting<boolean>;
    implementation: Setting<string>;
    implementationName: string;
    fields: (ProviderField & { errors: object[]; pending: boolean })[];
  };
}

// The instance being edited, or the new one picked from the schema (no id), with its changes, as Servarr's provider
// modals read it (createProviderSettingsSelector).
export function useMediaManagerSettings(id?: number): MediaManagerSettings {
  const selector = useMemo(
    () => createProviderSettingsSelector('mediaManagers'),
    []
  );

  return useSelector(
    (state: AppState) =>
      selector(state, { id }) as unknown as MediaManagerSettings
  );
}

// Servarr's provider field, typed loosely: it hands any other prop to its input.
const ProviderFieldGroup = ProviderFieldFormGroup as unknown as ComponentType<
  Record<string, unknown>
>;

// Servarr's value actions, typed: their payload creators are JavaScript.
const setValue = setMediaManagerValue as unknown as (change: SettingChange) => {
  type: string;
};
const setFieldValue = setMediaManagerFieldValue as unknown as (
  change: SettingChange
) => { type: string };

interface MediaManagerFormProps {
  settings: MediaManagerSettings;
  isReadOnly: boolean;
}

// A Sonarr or Radarr's settings, as Prowlarr's application modal shows them: its name, whether it's turned on, then
// its provider fields from the schema (ProviderFieldFormGroup), Sonarr's own and its anime settings only for Sonarr.
// Problems saving or testing show at the top, as Servarr's forms show them.
function MediaManagerForm({ settings, isReadOnly }: MediaManagerFormProps) {
  const dispatch = useDispatch();
  const advancedSettings = useSelector(
    (state: AppState) => state.settings.advancedSettings
  );
  const { item, validationErrors, validationWarnings } = settings;
  const isSonarr = item.implementation.value === SONARR;
  const locked = readOnlyProps(isReadOnly);

  const onInputChange = useCallback(
    (change: SettingChange) => dispatch(setValue(change)),
    [dispatch]
  );
  const onFieldChange = useCallback(
    (change: SettingChange) => dispatch(setFieldValue(change)),
    [dispatch]
  );

  const fieldsIn = (section?: string) =>
    item.fields
      .filter((field) => field.section === section)
      .map((field) => (
        <ProviderFieldGroup
          key={field.name}
          advancedSettings={advancedSettings}
          provider="mediamanager"
          providerData={item}
          section={MEDIA_MANAGERS_SECTION}
          {...field}
          {...locked}
          onChange={onFieldChange}
        />
      ));

  return (
    <Form
      validationErrors={validationErrors}
      validationWarnings={validationWarnings}
    >
      <FormGroup>
        <FormLabel>{translate('BackendName')}</FormLabel>
        <FormInputGroup
          type={inputTypes.TEXT}
          name="name"
          helpText={translate('BackendNameHelpText')}
          {...item.name}
          {...locked}
          onChange={onInputChange}
        />
      </FormGroup>

      <FormGroup>
        <FormLabel>{translate('BackendEnabled')}</FormLabel>
        <FormInputGroup
          type={inputTypes.CHECK}
          name="enable"
          helpText={translate('BackendEnabledHelpText')}
          {...item.enable}
          {...locked}
          onChange={onInputChange}
        />
      </FormGroup>

      {fieldsIn(undefined)}
      {isSonarr && fieldsIn(SONARR_SECTION)}

      {isSonarr && (
        <FieldSet legend={translate('InstanceAnime')}>
          {fieldsIn(ANIME_SECTION)}
        </FieldSet>
      )}
    </Form>
  );
}

export default MediaManagerForm;
