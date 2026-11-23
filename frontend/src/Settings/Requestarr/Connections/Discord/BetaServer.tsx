import React from 'react';
import FieldSet from 'Components/FieldSet';
import translate from 'Utilities/String/translate';

const POINTS = [
  'DiscordBetaServerBugs',
  'DiscordBetaServerMark',
  'DiscordBetaServerPosts',
  'DiscordBetaServerName',
];

// This server is beta because it was installed from the beta image: shown, never chosen, with what that means.
function BetaServer() {
  return (
    <FieldSet legend={translate('DiscordBetaServer')}>
      <p>{translate('DiscordBetaServerIntro')}</p>
      <ul>
        {POINTS.map((point) => (
          <li key={point}>{translate(point)}</li>
        ))}
      </ul>
    </FieldSet>
  );
}

export default BetaServer;
