import { useEffect, useState } from 'react';
import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import { titleText } from '../portalText';
import { PortalSearchResults } from '../PortalTypes';

// Typing pauses this long before the portal is asked, so each key doesn't send a search.
const TYPING_PAUSE_MS = 250;

export interface SearchItem {
  group: string;
  label: string;
  detail: string | null;
  path: string;
}

// The header search's results as one list, requests first then members, each with the page it opens.
export function searchItems(results?: PortalSearchResults): SearchItem[] {
  if (!results) {
    return [];
  }

  const requests = results.requests.map((request) => ({
    group: translate('PortalRequests'),
    label: titleText(request),
    detail: request.requester.name,
    path: `/requests/${request.id}`,
  }));

  const members = results.members.map((member) => ({
    group: translate('PortalMembers'),
    label: member.name,
    detail: member.discordName ? `@${member.discordName}` : null,
    path: `/members/${member.id}`,
  }));

  return [...requests, ...members];
}

// What Requestarr has for the words typed in the header: requests by title, members by name or Discord username.
// Nothing while the box is empty.
function usePortalSearch(term: string) {
  const [results, setResults] = useState<PortalSearchResults>();
  const wanted = term.trim();

  useEffect(() => {
    if (!wanted) {
      return undefined;
    }

    const pause = setTimeout(() => {
      callApi<PortalSearchResults>(
        `/portal/search?term=${encodeURIComponent(wanted)}`
      ).then(setResults, () => setResults(undefined));
    }, TYPING_PAUSE_MS);

    return () => clearTimeout(pause);
  }, [wanted]);

  return wanted ? results : undefined;
}

export default usePortalSearch;
