import translate from 'Utilities/String/translate';
import { AccessState } from './PortalTypes';

const LEVELS: [AccessState, string][] = [
  ['off', 'PortalAccessOff'],
  ['needsApproval', 'PortalAccessNeedsApproval'],
  ['autoApprove', 'PortalAccessAutoApprove'],
];

// The access levels as a select's choices, in the member card's words (translated when shown, once they've loaded).
function accessLevelChoices() {
  return LEVELS.map(([key, words]) => ({ key, value: translate(words) }));
}

export default accessLevelChoices;
