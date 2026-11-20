import { useEffect, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import { titleText } from '../portalText';
import { PortalMedia, PortalPerson, PortalSearchResults } from '../PortalTypes';

// Typing pauses this long before the portal is asked, so each key doesn't send a search.
const TYPING_PAUSE_MS = 250;

export interface SearchItem {
  group: string;
  label: string;
  path: string;
  media?: PortalMedia | null;
  person?: { avatar: string | null; handle: string | null };
  requester?: PortalPerson;
}

// The header search's results as one list, requests first then members, each with the page it opens.
export function searchItems(results?: PortalSearchResults): SearchItem[] {
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

  const members = results.members.map((member) => ({
    group: translate('PortalMembers'),
    label: member.name,
    path: `/members/${member.id}`,
    person: { avatar: member.avatar, handle: member.discordName },
  }));

  return [...requests, ...members];
}

// What Requestarr has for the words typed in the header: requests by title, members by name or Discord username.
// Nothing while the box is empty or until the answer for what's typed now arrives.
function usePortalSearch(term: string) {
  // Each answer remembers what it was for: a slow answer to an earlier term never replaces a later one.
  const [answer, setAnswer] = useState<{
    term: string;
    results?: PortalSearchResults;
  }>();
  const wanted = term.trim();

  useEffect(() => {
    if (!wanted) {
      return undefined;
    }

    const pause = setTimeout(() => {
      callApi<PortalSearchResults>(
        `/portal/search?term=${encodeURIComponent(wanted)}`
      ).then(
        (results) => setAnswer({ term: wanted, results }),
        () => setAnswer({ term: wanted })
      );
    }, TYPING_PAUSE_MS);

    return () => clearTimeout(pause);
  }, [wanted]);

  return answer?.term === wanted ? answer.results : undefined;
}

export default usePortalSearch;
