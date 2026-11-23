import React, { ComponentType, useCallback } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import FieldSet from 'Components/FieldSet';
import Form from 'Components/Form/Form';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import ProviderFieldFormGroup from 'Components/Form/ProviderFieldFormGroup';
import { inputTypes } from 'Helpers/Props';
import {
  setMediaManagerFieldValue,
  setMediaManagerValue,
} from 'Store/Actions/settingsActions';
import translate from 'Utilities/String/translate';
import readOnlyProps from '../../readOnlyProps';
import { linkPreview, pageLink, updatesText } from './MediaManagerExtras';
import {
  MEDIA_MANAGERS_SECTION,
  MediaManagerItem,
  MediaManagerSettings,
  portOf,
} from './mediaManagerSettings';
import { PURPOSE_FIELD, purposeChoices, usePurpose } from './purpose';
import useChoices, { problemOf } from './useChoices';

export {
  MEDIA_MANAGERS_SECTION,
  useMediaManagerSettings,
} from './mediaManagerSettings';

// The schema's sections (MediaManagerActions on the server): Sonarr's own fields and the anime ones.
const SONARR_SECTION = 'sonarr';
const ANIME_SECTION = 'anime';
const SONARR = 'Sonarr';

interface SettingChange {
  name: string;
  value: unknown;
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

type Field = MediaManagerItem['fields'][number];
type Choices = ReturnType<typeof useChoices>;

interface TwoLineCheckProps {
  label: string;
  lines: string[];
  name: string;
  value: unknown;
  locked: object;
  onChange: (change: SettingChange) => void;
}

// A checkbox whose help is two lines under it: what On and what Off mean.
function TwoLineCheck(props: TwoLineCheckProps) {
  return (
    <FormGroup>
      <FormLabel>{props.label}</FormLabel>
      <div>
        <FormInputGroup
          type={inputTypes.CHECK}
          name={props.name}
          value={props.value}
          {...props.locked}
          onChange={props.onChange}
        />
        {props.lines.map((line) => (
          <FormInputHelpText key={line} text={line} />
        ))}
      </div>
    </FormGroup>
  );
}

// The words a field takes from this instance's type (its port, its own name), which the shared schema can't.
function fieldWords(field: Field, app: string): Record<string, unknown> {
  switch (field.name) {
    case 'url':
      return {
        placeholder: translate('InstanceUrlPlaceholder', { port: portOf(app) }),
        helpText: translate('InstanceUrlHelpText', { port: portOf(app) }),
      };
    case 'apiKey':
      return { helpText: translate('InstanceApiKeyHelpText', { app }) };
    case 'animePriority':
      return { helpText: translate('InstanceAnimePriorityHelpText', { app }) };
    default:
      return {};
  }
}

interface FieldExtrasProps {
  item: MediaManagerItem;
  app: string;
  choices: Choices;
  purpose: ReturnType<typeof usePurpose>;
}

// Why a list couldn't load, as one of its field's errors; nothing before there's an address to try.
function loadErrors(
  field: Field,
  list: Choices['profiles'],
  item: MediaManagerItem
) {
  const hasAddress = item.fields.some((f) => f.name === 'url' && f.value);
  const problem = hasAddress ? problemOf(list) : undefined;

  return {
    errors: problem ? [...field.errors, { message: problem }] : field.errors,
  };
}

// What a field shows under it, in its own column: why its list couldn't load and a link to the instance's page the
// list comes from (Servarr's help link), or the link a request's Open button will make.
function fieldExtras(field: Field, { item, app, choices }: FieldExtrasProps) {
  switch (field.name) {
    case 'qualityProfile':
      return {
        ...loadErrors(field, choices.profiles, item),
        ...pageLink(
          item,
          'settings/profiles',
          translate('InstanceOpenProfiles', { app })
        ),
      };
    case 'rootFolder':
      return {
        ...loadErrors(field, choices.folders, item),
        ...pageLink(
          item,
          'settings/mediamanagement',
          translate('InstanceOpenMediaManagement', { app })
        ),
      };
    case 'browserUrl':
      return { helpTexts: linkPreview(item, app) };
    default:
      return {};
  }
}

interface FieldProps extends FieldExtrasProps {
  field: Field;
  locked: object;
  onChange: (change: SettingChange) => void;
}

// One provider field from the schema (ProviderFieldFormGroup), with this type's words and what it shows under it.
function MediaManagerField({ field, locked, onChange, ...extras }: FieldProps) {
  const advancedSettings = useSelector(
    (state: AppState) => state.settings.advancedSettings
  );

  if (field.name === PURPOSE_FIELD && !extras.purpose.isShown) {
    return null;
  }

  if (field.name === 'animeEnabled') {
    return (
      <TwoLineCheck
        label={field.label ?? ''}
        lines={[
          translate('InstanceAnimeOn', { app: extras.app }),
          translate('InstanceAnimeOff'),
        ]}
        name={field.name}
        value={field.value}
        locked={locked}
        onChange={onChange}
      />
    );
  }

  return (
    <ProviderFieldGroup
      advancedSettings={advancedSettings}
      provider="mediamanager"
      providerData={extras.item}
      section={MEDIA_MANAGERS_SECTION}
      {...field}
      {...fieldWords(field, extras.app)}
      {...fieldExtras(field, extras)}
      {...(field.name === PURPOSE_FIELD && {
        ...extras.purpose.words,
        selectOptions: purposeChoices(
          field.selectOptions,
          extras.purpose.allowsAnime
        ),
      })}
      {...locked}
      onChange={onChange}
    />
  );
}

interface MediaManagerFormProps {
  settings: MediaManagerSettings;
  isReadOnly: boolean;
}

// A Sonarr or Radarr's settings, as Prowlarr's application modal shows them: how its downloads reach Requestarr, its
// name, whether it's in use (a saved one only: a new one is), then its provider fields from the schema, Sonarr's own
// and its anime settings only for Sonarr. Advanced fields stay in their place, shown by Show Advanced in the footer.
function MediaManagerForm({ settings, isReadOnly }: MediaManagerFormProps) {
  const dispatch = useDispatch();
  const { item, validationErrors, validationWarnings } = settings;
  const app = item.implementation.value;
  const locked = readOnlyProps(isReadOnly);
  const choices = useChoices(item);
  const purpose = usePurpose(item, app);
  const updates = item.updates?.value;

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
        <MediaManagerField
          key={field.name}
          field={field}
          item={item}
          app={app}
          choices={choices}
          purpose={purpose}
          locked={locked}
          onChange={onFieldChange}
        />
      ));

  return (
    <Form
      validationErrors={validationErrors}
      validationWarnings={validationWarnings}
    >
      {updates && <FormInputHelpText text={updatesText(updates, app)} />}

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

      {!!item.id?.value && (
        <TwoLineCheck
          label={translate('InstanceUse', { app })}
          lines={[translate('InstanceUseOn'), translate('InstanceUseOff')]}
          name="enable"
          value={item.enable.value}
          locked={locked}
          onChange={onInputChange}
        />
      )}

      {fieldsIn(undefined)}
      {app === SONARR && fieldsIn(SONARR_SECTION)}

      {app === SONARR && (
        <FieldSet legend={translate('InstanceAnime')}>
          {fieldsIn(ANIME_SECTION)}
        </FieldSet>
      )}
    </Form>
  );
}

export default MediaManagerForm;
