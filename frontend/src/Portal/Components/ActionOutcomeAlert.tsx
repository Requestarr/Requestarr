import React from 'react';
import Alert from 'Components/Alert';
import { kinds } from 'Helpers/Props';
import { plainText } from '../portalText';
import { ActionOutcome } from '../usePortalAction';

// What Requestarr said about the last action: what it did, or why it refused.
function ActionOutcomeAlert({ outcome }: { outcome?: ActionOutcome }) {
  if (!outcome) {
    return null;
  }

  return (
    <Alert kind={outcome.isDone ? kinds.SUCCESS : kinds.DANGER}>
      {plainText(outcome.message)}
    </Alert>
  );
}

export default ActionOutcomeAlert;
