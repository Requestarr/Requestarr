import { useEffect, useRef } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import { setMediaManagerFieldValue } from 'Store/Actions/settingsActions';
import MediaManager from 'typings/MediaManager';
import translate from 'Utilities/String/translate';
import { MediaManagerItem } from './mediaManagerSettings';

// What an instance is for (InstancePurpose on the server), by the number Servarr's form picks it by.
export const PURPOSE_FIELD = 'purposeChoice';
const EVERYTHING = 0;
const ANIME = 1;
const NOTHING = 2;

const RADARR = 'Radarr';

// Each purpose's words: the one for whatever no other instance takes is said by what it takes, shows or movies.
function purposeWords(app: string): Record<number, string> {
  return {
    [EVERYTHING]:
      app === RADARR
        ? 'InstancePurposeLabelAllOtherMovies'
        : 'InstancePurposeLabelAllOtherShows',
    [ANIME]: 'InstancePurposeLabelAnime',
    [NOTHING]: 'InstancePurposeLabelNothing',
  };
}

const setFieldValue = setMediaManagerFieldValue as unknown as (change: {
  name: string;
  value: unknown;
}) => { type: string };

function purposeOf(instance: { fields: { name: string; value: unknown }[] }) {
  return instance.fields.find((field) => field.name === PURPOSE_FIELD)
    ?.value as number | undefined;
}

// The card label's words for what an instance is for, once its type has more than one.
export function purposeLabelKey(item: MediaManager, siblings: MediaManager[]) {
  const sameType = siblings.filter(
    (other) => other.implementation === item.implementation
  );

  return sameType.length > 1
    ? purposeWords(item.implementation)[purposeOf(item) ?? EVERYTHING]
    : undefined;
}

// The other instances of this one's type, saved and not deleted.
function useOthers(item: MediaManagerItem) {
  const items = useSelector(
    (state: AppState) => state.settings.mediaManagers.items
  );

  return items.filter(
    (other) =>
      other.implementation === item.implementation.value &&
      other.id !== item.id?.value
  );
}

// The purpose field's words: shown only when the type has more than one instance (alone, an instance takes all of its
// type's requests), no Anime for Radarr, and what saving would move from another instance. A new instance added beside
// others starts on Nothing yet, so it never takes the others' requests by accident.
export function usePurpose(item: MediaManagerItem, app: string) {
  const dispatch = useDispatch();
  const others = useOthers(item);
  const chosen = purposeOf(item);
  const allowsAnime = app === 'Sonarr';
  const isNew = !item.id?.value;
  const hasOthers = others.length > 0;
  // Settled once, when the form's value first arrives: the admin can still choose all other afterwards.
  const settled = useRef(false);

  useEffect(() => {
    if (settled.current || chosen === undefined) {
      return;
    }

    settled.current = true;

    if (isNew && hasOthers && chosen === EVERYTHING) {
      dispatch(setFieldValue({ name: PURPOSE_FIELD, value: NOTHING }));
    }
  }, [dispatch, isNew, hasOthers, chosen]);

  const holder =
    chosen === NOTHING
      ? undefined
      : others.find((o) => purposeOf(o) === chosen);
  const words = purposeWords(app);

  return {
    isShown: others.length > 0,
    words: {
      label: translate('InstancePurpose', { app }),
      helpTexts: [
        translate('InstancePurposeAllOtherHelp', {
          app,
          purpose: translate(words[EVERYTHING]),
        }),
        ...(allowsAnime ? [translate('InstancePurposeAnimeHelp')] : []),
        translate('InstancePurposeNothingHelp'),
      ],
      helpTextWarning: holder
        ? translate('InstancePurposeMoves', {
            name: holder.name,
            purpose: translate(words[chosen ?? EVERYTHING]),
          })
        : undefined,
    },
    allowsAnime,
    labels: words,
  };
}

// The purpose's choices this type offers, in its words: Anime only for Sonarr.
export function purposeChoices<T extends { value: number; name?: string }>(
  options: T[] | undefined,
  purpose: ReturnType<typeof usePurpose>
) {
  return options
    ?.filter((option) => purpose.allowsAnime || option.value !== ANIME)
    .map((option) => ({
      ...option,
      name: translate(purpose.labels[option.value]),
    }));
}

// What the confirm says before a save leaves the type with nothing for its other requests: the saved instance takes
// them, others of its type exist, and it's being turned off or given another purpose. Undefined when nothing stops.
export function useStopsAllOther(item: MediaManagerItem, app: string) {
  const others = useOthers(item);
  const saved = useSelector((state: AppState) =>
    state.settings.mediaManagers.items.find((one) => one.id === item.id?.value)
  );
  const tookAllOther = saved?.enable && purposeOf(saved) === EVERYTHING;
  const keeps = item.enable.value && purposeOf(item) === EVERYTHING;

  if (!tookAllOther || keeps || !others.length) {
    return undefined;
  }

  return translate(
    app === RADARR
      ? 'InstanceAllOtherStopsMovies'
      : 'InstanceAllOtherStopsShows',
    { app }
  );
}
