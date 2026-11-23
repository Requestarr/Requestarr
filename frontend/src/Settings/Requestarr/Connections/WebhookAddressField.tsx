import React, { ComponentType, useCallback, useEffect, useState } from 'react';
import FormGroup from 'Components/Form/FormGroup';
import FormInputButton, {
  FormInputButtonProps,
} from 'Components/Form/FormInputButton';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import FormLabel from 'Components/Form/FormLabel';
import { inputTypes } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import readOnlyProps from '../readOnlyProps';
import { callRequestarrApi } from '../requestarrApi';
import useApiAction from '../useApiAction';
import { SettingsForm } from '../useSettingsForm';

const NAME = 'availabilityWebhookUrl';

const LOCAL_HOSTS = ['localhost', '127.0.0.1', '[::1]'];
const IP_ADDRESS = /^\d{1,3}(\.\d{1,3}){3}$/;

// Servarr's input button, typed with the spinner it hands on when it can spin.
const SpinningButton = FormInputButton as ComponentType<
  FormInputButtonProps & { isSpinning: boolean }
>;

// One instance's Connect test of the address (WebhookReach on the server).
interface Reach {
  id: number;
  name: string;
  problem?: string;
}

// The address the admin's browser reaches Requestarr at (with its URL base): the container can't know its outside
// address, but the browser can. Local: localhost, which media managers can't use. Check: a name rather than an IP
// address, likely a reverse proxy's domain, so the admin should check it.
function browserAddress() {
  const { hostname, origin } = window.location;

  return {
    url: `${origin}${window.Requestarr.urlBase}`,
    host: hostname,
    isLocal: LOCAL_HOSTS.includes(hostname),
    needsCheck: !IP_ADDRESS.test(hostname),
  };
}

// Fills an empty saved address from the browser's, once, unless that's localhost; a saved value is never changed.
// What to tell the admin about it: that localhost can't be used, or to check a name the browser used.
function usePrefill(form: SettingsForm) {
  const saved = form.settings?.values[NAME];
  const isProvisioned = !!form.settings?.provisioned;
  const { onChange } = form;
  const browser = browserAddress();
  const shouldFill = saved === '' && !isProvisioned && !browser.isLocal;

  useEffect(() => {
    if (shouldFill) {
      onChange({ name: NAME, value: browser.url });
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- once, when the saved settings arrive
  }, [shouldFill]);

  if (saved !== '' || isProvisioned) {
    return undefined;
  }

  if (browser.isLocal) {
    return translate('WebhookAddressLocalhost', { host: browser.host });
  }

  return browser.needsCheck ? translate('WebhookAddressCheckThis') : undefined;
}

// Requestarr's address for media managers, as Servarr shows a field with a button beside it: prefilled from the
// browser's address, and Test has each media manager in use try its own Connect test of it, saving nothing.
function WebhookAddressField({ form }: { form: SettingsForm }) {
  const note = usePrefill(form);
  const [reached, setReached] = useState<Reach[]>();
  const test = useApiAction();
  const { run } = test;
  const settings = form.settings;
  const value =
    NAME in form.pending ? form.pending[NAME] : settings?.values[NAME];

  const onTest = useCallback(() => {
    run(
      () =>
        callRequestarrApi<Reach[]>('settings/webhook-test', 'POST', {
          url: value,
        }),
      setReached
    );
  }, [run, value]);

  if (!settings) {
    return null;
  }

  return (
    <FormGroup>
      <FormLabel>{translate('SettingAvailabilityWebhookUrl')}</FormLabel>
      <div>
        <FormInputGroup
          type={inputTypes.TEXT}
          name={NAME}
          value={value}
          helpText={translate('SettingAvailabilityWebhookUrlHelpText')}
          buttons={[
            <SpinningButton
              key="test"
              canSpin={true}
              isSpinning={test.isBusy}
              isDisabled={!value || settings.provisioned}
              onPress={onTest}
            >
              {translate('WebhookAddressTest')}
            </SpinningButton>,
          ]}
          {...readOnlyProps(settings.provisioned)}
          onChange={form.onChange}
        />
        {note && <FormInputHelpText text={note} isWarning={true} />}
        {test.problem && (
          <FormInputHelpText text={test.problem} isError={true} />
        )}
        {reached?.length === 0 && (
          <FormInputHelpText text={translate('WebhookAddressNothing')} />
        )}
        {reached?.map((reach) => (
          <FormInputHelpText
            key={reach.id}
            text={
              reach.problem
                ? translate('WebhookAddressFailed', {
                    name: reach.name,
                    problem: reach.problem,
                  })
                : translate('WebhookAddressReached', { name: reach.name })
            }
            isError={!!reach.problem}
          />
        ))}
      </div>
    </FormGroup>
  );
}

export default WebhookAddressField;
