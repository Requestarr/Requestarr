import { Dispatch } from 'redux';
import { messageTypes } from 'Helpers/Props';
import { hideMessage, showMessage } from 'Store/Actions/appActions';
import { plainText } from './portalText';

// How long a result stays in the sidebar, as Servarr keeps a finished command's: longer when something failed.
const MESSAGE_SECONDS = 4;
const FAILURE_SECONDS = 10;

export interface ActionOutcome {
  isDone: boolean;
  message: string;
}

// Requestarr's answer to an action in the sidebar's messages, as Servarr reports a finished command: what it did, or
// why it refused. id names the kind of action; a new answer replaces the last one of that kind and shows on top.
function showOutcome(dispatch: Dispatch, id: string, outcome: ActionOutcome) {
  dispatch(hideMessage({ id }));
  dispatch(
    showMessage({
      id,
      name: id,
      message: plainText(outcome.message),
      type: outcome.isDone ? messageTypes.SUCCESS : messageTypes.ERROR,
      hideAfter: outcome.isDone ? MESSAGE_SECONDS : FAILURE_SECONDS,
    })
  );
}

export default showOutcome;
