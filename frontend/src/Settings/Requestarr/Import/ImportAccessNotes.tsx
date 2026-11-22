import React from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { ImportAccess } from './importTypes';

// Above the channels, only when it matters: the bot may not read other bots' messages, with where to turn that on.
function ImportAccessNotes({ access }: { access?: ImportAccess }) {
  if (access?.messageContent !== false) {
    return null;
  }

  return (
    <Alert kind={kinds.WARNING}>
      {translate('ImportMessageContentOff')}{' '}
      {access.portalUrl && (
        <Link to={access.portalUrl}>
          {translate('ImportOpenDeveloperPortal')}
        </Link>
      )}
    </Alert>
  );
}

export default ImportAccessNotes;
