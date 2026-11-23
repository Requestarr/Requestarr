import React from 'react';
import FormInputHelpText from 'Components/Form/FormInputHelpText';
import Link from 'Components/Link/Link';
import translate from 'Utilities/String/translate';
import { InstanceUpdates, MediaManagerItem } from './mediaManagerSettings';
import { problemOf } from './useChoices';
import styles from './MediaManagerExtras.css';

// An example title for the link preview, used until the library has one.
const EXAMPLE_SLUG: Record<string, string> = {
  Sonarr: 'series/the-office',
  Radarr: 'movie/the-matrix',
};

function fieldValue(item: MediaManagerItem, name: string) {
  return item.fields.find((field) => field.name === name)?.value as
    | string
    | undefined;
}

// Where an admin opens the instance themselves: the address for links when it's set, otherwise the address.
function linkBase(item: MediaManagerItem) {
  const base = fieldValue(item, 'browserUrl') || fieldValue(item, 'url');

  return base ? base.replace(/\/+$/, '') : undefined;
}

// A link to the instance's own page a list's choices come from, once there's an address to build it from.
export function PageLink({
  item,
  path,
  label,
}: {
  item: MediaManagerItem;
  path: string;
  label: string;
}) {
  const base = linkBase(item);

  return base ? (
    <div className={styles.extra}>
      <Link to={`${base}/${path}`} target="_blank">
        {label}
      </Link>
    </div>
  ) : null;
}

// Why a list that needs the instance couldn't load, in the server's plain words.
export function LoadProblem({
  list,
}: {
  list?: Parameters<typeof problemOf>[0];
}) {
  const problem = problemOf(list);

  return problem ? <FormInputHelpText text={problem} isError={true} /> : null;
}

// The link a request's Open button makes from what's typed, updated as the admin types.
export function LinkPreview({
  item,
  app,
}: {
  item: MediaManagerItem;
  app: string;
}) {
  const base = linkBase(item);

  if (!base) {
    return null;
  }

  const url = `${base}/${EXAMPLE_SLUG[app] ?? EXAMPLE_SLUG.Sonarr}`;

  return <FormInputHelpText text={translate('InstanceLinkPreview', { url })} />;
}

const UPDATES_TEXT: Record<InstanceUpdates['reason'], string> = {
  rightAway: 'InstanceUpdatesRightAway',
  unreachable: 'InstanceUpdatesUnreachable',
  noAddress: 'InstanceUpdatesNoAddress',
  setByFile: 'InstanceUpdatesSetByFile',
  notChecked: 'InstanceUpdatesNotChecked',
};

// How the instance tells Requestarr about downloads, and why: right away, or on the timer.
export function updatesText(updates: InstanceUpdates, app: string) {
  return translate(UPDATES_TEXT[updates.reason], {
    minutes: updates.pollMinutes,
    app,
  });
}
