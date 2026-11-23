import { useEffect, useMemo } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import { fetchOptions } from 'Store/Actions/providerOptionActions';
import { setMediaManagerFieldValue } from 'Store/Actions/settingsActions';
import { MediaManagerItem } from './mediaManagerSettings';

// The provider actions behind the two lists that need the instance itself (MediaManagerActions on the server).
const QUALITY_PROFILES = 'getQualityProfiles';
const ROOT_FOLDERS = 'getRootFolders';

// Typing settles for this long before the lists load again.
const LOAD_DELAY_MILLISECONDS = 600;

// Sonarr's and Radarr's built-in quality profile, matched by name.
const ANY_PROFILE = 'any';

interface ChoiceOption {
  value: string;
  name: string;
}

interface ChoiceList {
  items: ChoiceOption[];
  isFetching: boolean;
  isPopulated: boolean;
  error?: { responseJSON?: { errorMessage?: string }[] | { message?: string } };
}

// Why a list couldn't load, as the server said it (the address, a network name, the API key), or nothing.
export function problemOf(list?: ChoiceList): string | undefined {
  const answer = list?.error?.responseJSON;

  if (!list?.error || list.isFetching) {
    return undefined;
  }

  return Array.isArray(answer) ? answer[0]?.errorMessage : answer?.message;
}

function useChoiceList(action: string): ChoiceList | undefined {
  return useSelector(
    (state: AppState) =>
      (state as unknown as { providerOptions: Record<string, ChoiceList> })
        .providerOptions[action]
  );
}

function fieldValue(item: MediaManagerItem, name: string) {
  return item.fields.find((field) => field.name === name)?.value;
}

const setFieldValue = setMediaManagerFieldValue as unknown as (change: {
  name: string;
  value: unknown;
}) => { type: string };

// The first choice to start a new instance with: the profile named Any (or the first when there's none), and the
// root folder when there's only one, so the admin picks only where there's a real choice.
function startingChoice(action: string, items: ChoiceOption[]) {
  if (action === QUALITY_PROFILES) {
    return (
      items.find((option) => option.name.toLowerCase() === ANY_PROFILE) ??
      items[0]
    )?.value;
  }

  return items.length === 1 ? items[0].value : undefined;
}

// Fills an empty field of a new instance from its list once it loads. A value the admin chose, or a saved one, is
// never changed.
function usePrefill(item: MediaManagerItem, action: string, field: string) {
  const dispatch = useDispatch();
  const list = useChoiceList(action);
  const isNew = !item.id?.value;
  const current = fieldValue(item, field);

  useEffect(() => {
    if (!isNew || current || !list?.isPopulated) {
      return;
    }

    const value = startingChoice(action, list.items);

    if (value) {
      dispatch(setFieldValue({ name: field, value }));
    }
  }, [dispatch, isNew, current, list, action, field]);

  return list;
}

// The two lists that need the instance, loaded as soon as the address and API key are filled in and again whenever
// they change (not only when a list is opened), so a list that can't load says why straight away.
function useChoices(item: MediaManagerItem) {
  const dispatch = useDispatch();
  const url = fieldValue(item, 'url');
  const apiKey = fieldValue(item, 'apiKey');
  const key = useMemo(() => `${url ?? ''} ${apiKey ?? ''}`, [url, apiKey]);

  useEffect(() => {
    if (!url) {
      return undefined;
    }

    const timer = setTimeout(() => {
      [QUALITY_PROFILES, ROOT_FOLDERS].forEach((action) =>
        dispatch(
          fetchOptions({
            section: action,
            action,
            provider: 'mediamanager',
            providerData: item,
          })
        )
      );
    }, LOAD_DELAY_MILLISECONDS);

    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- only the address and key decide what the lists hold
  }, [dispatch, key]);

  return {
    profiles: usePrefill(item, QUALITY_PROFILES, 'qualityProfile'),
    folders: usePrefill(item, ROOT_FOLDERS, 'rootFolder'),
  };
}

export default useChoices;
