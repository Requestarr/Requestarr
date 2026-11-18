import { accessStateText } from './portalText';
import { AccessState } from './PortalTypes';

const LEVELS: AccessState[] = ['off', 'needsApproval', 'autoApprove'];

// The access levels as a select's choices, in the member card's words (translated when shown, once they've loaded).
function accessLevelChoices() {
  return LEVELS.map((key) => ({ key, value: accessStateText(key) }));
}

export default accessLevelChoices;
