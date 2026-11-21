import runBulkAction from '../Components/runBulkAction';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { actionLabel } from './ActionButton';

// What the toolbar can do to every request picked.
export const BULK_ACTIONS = ['approve', 'deny', 'remove'];

// The action on every request picked, in one call to Requestarr's bulk address.
function runBulk(action: string, requests: PortalRequest[], body: object) {
  return runBulkAction(requests, {
    path: '/portal/requests/bulk',
    body: { ...body, action },
    label: actionLabel(action),
    nameOf: titleText,
  });
}

export default runBulk;
