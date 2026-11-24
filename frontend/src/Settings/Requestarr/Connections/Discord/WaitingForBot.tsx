import React from 'react';
import Icon from 'Components/Icon';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';

// Shown once Discord's invite page is open, until the bot joins and the window closes.
function WaitingForBot() {
  return (
    <p>
      <Icon name={icons.SPINNER} isSpinning={true} />{' '}
      {translate('DiscordWaitingForBot')}
    </p>
  );
}

export default WaitingForBot;
