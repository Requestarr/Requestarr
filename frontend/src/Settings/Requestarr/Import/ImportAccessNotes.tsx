import React from 'react';
import Alert from 'Components/Alert';
import Link from 'Components/Link/Link';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { ImportAccess } from './importTypes';
import styles from './ImportPage.css';

// Above the channels: whether the bot may read other bots' messages (with where to turn that on), and the link that
// adds the bot to another server only to read it.
function ImportAccessNotes({ access }: { access?: ImportAccess }) {
  if (!access) {
    return null;
  }

  return (
    <>
      {access.messageContent === false && (
        <Alert kind={kinds.WARNING}>
          {translate('ImportMessageContentOff')}{' '}
          {access.portalUrl && (
            <Link to={access.portalUrl}>
              {translate('ImportOpenDeveloperPortal')}
            </Link>
          )}
        </Alert>
      )}

      {access.readInviteUrl && (
        <div className={styles.invite}>
          <Link to={access.readInviteUrl}>{translate('ImportReadInvite')}</Link>
          <div className={styles.inviteHelp}>
            {translate('ImportReadInviteHelpText')}
          </div>
        </div>
      )}
    </>
  );
}

export default ImportAccessNotes;
