import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { AppSettings, Instance, SetupStatus } from '../RequestarrSettingsTypes';
import { CardLabel } from './ConnectionCard';
import { TestOutcome } from './useTestAll';

// The cards' status labels, in the tag style of Sonarr's profiles: green when connected, red when failing, grey for
// the rest.
function connected(): CardLabel {
  return { text: translate('ConnectionConnected'), kind: kinds.SUCCESS };
}

function failing(): CardLabel {
  return { text: translate('ConnectionFailing'), kind: kinds.DANGER };
}

function grey(key: string): CardLabel {
  return { text: translate(key), kind: kinds.DISABLED };
}

function provisioned(settings: AppSettings | undefined): CardLabel[] {
  return settings?.provisioned ? [grey('ConnectionProvisioned')] : [];
}

function botStatus(setup: SetupStatus | undefined): CardLabel[] {
  switch (setup?.botState) {
    case undefined:
      return [];
    case 'connected':
      return [connected()];
    case 'failed':
    case 'disconnected':
      return [failing()];
    case 'connecting':
      return [grey('BotStateConnecting')];
    default:
      return [grey('ConnectionNotSetUp')];
  }
}

export function discordLabels(
  setup: SetupStatus | undefined,
  settings: AppSettings | undefined
): CardLabel[] {
  return [...botStatus(setup), ...provisioned(settings)];
}

// Test All Connections' outcome when it tested this instance, otherwise Requestarr's last check.
function instanceStatus(instance: Instance, tested?: TestOutcome): CardLabel {
  if (!instance.settings.enabled) {
    return grey('InstanceTurnedOff');
  }

  if (tested) {
    return tested.problem ? failing() : connected();
  }

  if (instance.connected) {
    return connected();
  }

  return instance.problem ? failing() : grey('ConnectionNotChecked');
}

export function instanceLabels(
  instance: Instance,
  settings: AppSettings | undefined,
  tested?: TestOutcome
): CardLabel[] {
  return [
    instanceStatus(instance, tested),
    ...(instance.settings.isDefault ? [grey('Default')] : []),
    ...provisioned(settings),
  ];
}
