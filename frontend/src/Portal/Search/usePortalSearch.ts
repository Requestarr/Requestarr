import { useEffect, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import { titleText } from '../portalText';
import {
  PortalLibraryTitle,
  PortalMedia,
  PortalPerson,
  PortalSearchResults,
} from '../PortalTypes';

// Typing pauses this long before the portal is asked, so each key doesn't send a search.
const TYPING_PAUSE_MS = 250;

// path: the page it opens; null for a library title, which has none (its result starts its removal instead).
export interface SearchItem {
  group: string;
  label: string;
  path: string | null;
  media?: PortalMedia | null;
  person?: { avatar: string | null; handle: string | null };
  requester?: PortalPerson;
  library?: PortalLibraryTitle;
}

// The header search's results as one list: requests, then library titles nobody requested here, then members.
export function searchItems(
  results?: PortalSearchResults,
  library: PortalLibraryTitle[] = []
): SearchItem[] {
  if (!results) {
    return [];
  }

  const requests = results.requests.map((request) => ({
    group: translate('PortalRequests'),
    label: titleText(request),
    path: `/requests/${request.id}`,
    media: request.media,
    requester: request.requester,
  }));

  const titles = library
    .filter((title) => !title.requested)
    .map((title) => ({
      group: translate('PortalInLibraryNotRequested'),
      label: title.title,
      path: null,
      library: title,
    }));

  const members = results.members.map((member) => ({
    group: translate('PortalMembers'),
    label: member.name,
    path: `/members/${member.id}`,
    person: { avatar: member.avatar, handle: member.discordName },
  }));

  return [...requests, ...titles, ...members];
}

// The portal's answer at /portal/{path}?term= for the words typed, once typing pauses. Each answer remembers what it
// was for: a slow answer to an earlier term never replaces a later one.
function useAnswer<T>(path: string, wanted: string) {
  const [answer, setAnswer] = useState<{ term: string; found?: T }>();

  useEffect(() => {
    if (!wanted) {
      return undefined;
    }

    const pause = setTimeout(() => {
      callApi<T>(`/portal/${path}?term=${encodeURIComponent(wanted)}`).then(
        (found) => setAnswer({ term: wanted, found }),
        () => setAnswer({ term: wanted })
      );
    }, TYPING_PAUSE_MS);

    return () => clearTimeout(pause);
  }, [path, wanted]);

  return answer?.term === wanted ? answer.found : undefined;
}

// What Requestarr has for the words typed in the header: requests by title, members by name or Discord username, and
// (asked separately, since it reads every Sonarr and Radarr) library titles. Nothing while the box is empty.
function usePortalSearch(term: string) {
  const wanted = term.trim();

  return {
    results: useAnswer<PortalSearchResults>('search', wanted),
    library: useAnswer<PortalLibraryTitle[]>('library', wanted),
  };
}

export default usePortalSearch;
