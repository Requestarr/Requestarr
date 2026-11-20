import { callApi } from 'Settings/Requestarr/requestarrApi';
import runEach from '../Components/runEach';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { actionLabel } from './ActionButton';

// What the selection bar can do to every request picked, by the path after each request's own.
export const BULK_PATHS: Record<string, string> = {
  approve: 'approve',
  deny: 'deny',
  remove: 'removal',
};

// The action on each request picked, as if pressed on each row.
function runBulk(action: string, requests: PortalRequest[], body: object) {
  return runEach(requests, {
    label: actionLabel(action),
    nameOf: titleText,
    call: (request) =>
      callApi(
        `/portal/requests/${request.id}/${BULK_PATHS[action]}`,
        'POST',
        body
      ),
  });
}

export default runBulk;
