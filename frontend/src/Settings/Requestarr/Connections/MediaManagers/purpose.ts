import { useEffect } from 'react';
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

const PURPOSE_WORDS: Record<number, string> = {
  [EVERYTHING]: 'InstancePurposeLabelEverything',
  [ANIME]: 'InstancePurposeLabelAnime',
  [NOTHING]: 'InstancePurposeLabelNothing',
};

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
    ? PURPOSE_WORDS[purposeOf(item) ?? EVERYTHING]
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

// The purpose field's words: shown only when the type has more than one instance (alone, an instance is for
// everything), no Anime for Radarr, and what saving would move from another instance. A new instance added beside
// others starts on Nothing yet, so it never takes everything by accident.
export function usePurpose(item: MediaManagerItem, app: string) {
  const dispatch = useDispatch();
  const others = useOthers(item);
  const chosen = purposeOf(item);
  const isNew = !item.id?.value;
  const startsOnEverything = isNew && others.length > 0 && chosen === EVERYTHING;

  useEffect(() => {
    if (startsOnEverything) {
      dispatch(setFieldValue({ name: PURPOSE_FIELD, value: NOTHING }));
    }
  }, [dispatch, startsOnEverything]);

  const holder =
    chosen === NOTHING ? undefined : others.find((o) => purposeOf(o) === chosen);

  return {
    isShown: others.length > 0,
    words: {
      label: translate('InstancePurpose', { app }),
      helpTexts: [
        translate('InstancePurposeEverythingHelp', { app }),
        ...(app === 'Sonarr' ? [translate('InstancePurposeAnimeHelp')] : []),
        translate('InstancePurposeNothingHelp'),
      ],
      helpTextWarning: holder
        ? translate('InstancePurposeMoves', {
            name: holder.name,
            purpose: translate(PURPOSE_WORDS[chosen ?? EVERYTHING]),
          })
        : undefined,
    },
    allowsAnime: app === 'Sonarr',
  };
}

// The purpose's choices this type offers: Anime only for Sonarr.
export function purposeChoices<T extends { value: number }>(
  options: T[] | undefined,
  allowsAnime: boolean
) {
  return options?.filter((option) => allowsAnime || option.value !== ANIME);
}
