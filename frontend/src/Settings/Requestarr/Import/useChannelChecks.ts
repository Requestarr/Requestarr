import { useEffect, useState } from 'react';
import { callRequestarrApi } from '../requestarrApi';
import { ImportChannel } from './importTypes';

// Wait this long after typing stops before checking the IDs.
const CHECK_DELAY_MILLISECONDS = 500;

export interface ChannelCheck {
  id: string;
  channel?: ImportChannel;
  problem?: string;
}

// The channel IDs in what was pasted: one per line or separated by commas or spaces.
export function channelIdsIn(text: string) {
  return [...new Set(text.split(/[\s,]+/).filter((id) => id.length > 0))];
}

// Each ID checked as it's entered: the channel, its server and whether the bot can read it, or why not.
function useChannelChecks(ids: string[]) {
  const [checks, setChecks] = useState<ChannelCheck[]>([]);
  const key = ids.join(',');

  useEffect(() => {
    const timer = setTimeout(() => {
      Promise.all(
        ids.map((id) =>
          callRequestarrApi<ImportChannel>(`import/channels/${id}`).then(
            (channel) => ({ id, channel }),
            (failure: Error) => ({ id, problem: failure.message })
          )
        )
      ).then(setChecks);
    }, CHECK_DELAY_MILLISECONDS);

    return () => clearTimeout(timer);
    // The IDs as text: a new array with the same IDs checks nothing again.
    // eslint-disable-next-line react-hooks/exhaustive-deps -- key stands for ids
  }, [key]);

  return checks;
}

export default useChannelChecks;
