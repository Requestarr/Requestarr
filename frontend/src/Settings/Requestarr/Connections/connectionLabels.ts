import { kinds } from 'Helpers/Props';
import MediaManager from 'typings/MediaManager';
import translate from 'Utilities/String/translate';
import {
  AppSettings,
  DiscordServer,
  SetupStatus,
} from '../RequestarrSettingsTypes';
import { CardLabel } from './ConnectionCard';
import { purposeLabelKey } from './MediaManagers/purpose';

// What testing one instance found: why it failed, or no problem when it passed.
export interface TestOutcome {
  id: number;
  name: string;
  problem?: string;
}

// The cards' status labels, in the tag style of Sonarr's profiles: green when connected, red when failing, grey for
// the rest.
function connected(): CardLabel {
  return { text: translate('ConnectionConnected'), kind: kinds.SUCCESS };
}

export function serverReach(server: DiscordServer): CardLabel {
  return server.reachable
    ? connected()
    : { text: translate('ConnectionNotReachable'), kind: kinds.DANGER };
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
function mediaManagerStatus(
  item: MediaManager,
  tested?: TestOutcome
): CardLabel {
  if (!item.enable) {
    return grey('InstanceTurnedOff');
  }

  if (tested) {
    return tested.problem ? failing() : connected();
  }

  if (item.connected) {
    return connected();
  }

  return item.problem ? failing() : grey('ConnectionNotChecked');
}

// Siblings: every instance, for what this one is for once its type has more than one.
export function mediaManagerLabels(
  item: MediaManager,
  settings: AppSettings | undefined,
  siblings: MediaManager[],
  tested?: TestOutcome
): CardLabel[] {
  const purpose = purposeLabelKey(item, siblings);

  return [
    mediaManagerStatus(item, tested),
    ...(purpose ? [grey(purpose)] : []),
    ...provisioned(settings),
  ];
}
