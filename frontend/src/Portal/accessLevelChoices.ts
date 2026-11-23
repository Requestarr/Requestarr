import { accessStateText } from './portalText';
import { AccessState } from './PortalTypes';

// Most open first, as every list of the three reads (AccessStates.Shown on the server).
const LEVELS: AccessState[] = ['autoApprove', 'needsApproval', 'off'];

// The access levels as a select's choices, in the member card's words (translated when shown, once they've loaded).
function accessLevelChoices() {
  return LEVELS.map((key) => ({ key, value: accessStateText(key) }));
}

export default accessLevelChoices;
