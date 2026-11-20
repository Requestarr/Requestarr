import { callApi } from 'Settings/Requestarr/requestarrApi';
import translate from 'Utilities/String/translate';
import { titleText } from '../portalText';
import { PortalRequest } from '../PortalTypes';
import { ActionOutcome } from '../usePortalAction';
import { actionLabel } from './ActionButton';

// What the selection bar can do to every request picked, by the path after each request's own.
export const BULK_PATHS: Record<string, string> = {
  approve: 'approve',
  deny: 'deny',
  remove: 'removal',
  deleteRecord: 'delete-record',
};

// One request's part: null when it worked, or what went wrong, by its title.
function runOne(request: PortalRequest, action: string, body: object) {
  return callApi(
    `/portal/requests/${request.id}/${BULK_PATHS[action]}`,
    'POST',
    body
  ).then(
    () => null,
    (failure: Error) => `${titleText(request)}: ${failure.message}`
  );
}

// The action on each request picked, one after another as if pressed on each row; a refusal doesn't stop the rest,
// and the outcome names each one refused and why.
function runBulk(
  action: string,
  requests: PortalRequest[],
  body: object
): Promise<ActionOutcome> {
  const failures = requests.reduce<Promise<string[]>>(
    (soFar, request) =>
      soFar.then((failed) =>
        runOne(request, action, body).then((failure) =>
          failure ? [...failed, failure] : failed
        )
      ),
    Promise.resolve([])
  );

  return failures.then((failed) => ({
    isDone: !failed.length,
    message: [
      translate('PortalBulkResult', {
        action: actionLabel(action),
        done: requests.length - failed.length,
        total: requests.length,
      }),
      ...failed,
    ].join(' '),
  }));
}

export default runBulk;
