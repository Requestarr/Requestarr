import { groupBy } from 'lodash';
import React, { useCallback, useState } from 'react';
import { useSelect } from 'App/SelectContext';
import CheckInput from 'Components/Form/CheckInput';
import Icon from 'Components/Icon';
import Link from 'Components/Link/Link';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { ImportProgress } from './importTypes';
import styles from './ImportChannels.css';

// A channel the bot can read, numbered for the page's SelectProvider (Discord's IDs are too long for its numbers).
export interface PickableChannel {
  id: number;
  channelId: string;
  server: string;
  name: string;
  hasSourceReplies: boolean;
}

interface ChannelProps {
  channel: PickableChannel;
  progress?: ImportProgress;
  isReading: boolean;
  isDisabled: boolean;
}

interface CheckChange {
  value: boolean;
  shiftKey?: boolean;
}

// A channel's box and name, with how many of its messages have been read once it has been.
function Channel({ channel, progress, isReading, isDisabled }: ChannelProps) {
  const [selectState, selectDispatch] = useSelect();
  const onChange = useCallback(
    ({ value, shiftKey = false }: CheckChange) =>
      selectDispatch({
        type: 'toggleSelected',
        id: channel.id,
        isSelected: value,
        shiftKey,
      }),
    [channel.id, selectDispatch]
  );

  return (
    <div className={styles.channel}>
      <CheckInput
        containerClassName={styles.check}
        name={channel.channelId}
        value={!!selectState.selectedState[channel.id]}
        isDisabled={isDisabled}
        onChange={onChange}
      />
      <span className={styles.name}>{`#${channel.name}`}</span>
      {progress && (
        <span className={styles.read}>
          {translate('ImportMessagesRead', {
            count: progress.read.toLocaleString(),
          })}
        </span>
      )}
      {isReading && (
        <Icon className={styles.read} name={icons.SPINNER} isSpinning={true} />
      )}
    </div>
  );
}

interface ImportChannelsProps {
  channels: PickableChannel[];
  progress: Record<string, ImportProgress>;

  // The channels the preview is still reading.
  reading: string[];
  isLoading: boolean;
  isDisabled: boolean;
}

// The channels Requestrr answered in (and any already ticked), or on request every channel the bot can read, under
// their server's name in as many narrow columns as fit. Ticking one reads it (once; Requestarr keeps what it read).
function ImportChannels(props: ImportChannelsProps) {
  const { channels, progress, reading, isLoading, isDisabled } = props;
  const [selectState] = useSelect();
  const [showAll, setShowAll] = useState(false);
  const onTogglePress = useCallback(() => setShowAll((all) => !all), []);
  const shown = showAll
    ? channels
    : channels.filter(
        (c) => c.hasSourceReplies || selectState.selectedState[c.id]
      );

  if (isLoading) {
    return <LoadingIndicator />;
  }

  return (
    <>
      {!shown.length && (
        <div className={styles.none}>
          {translate(
            channels.length
              ? 'ImportNoRequestrrChannels'
              : 'ImportNoReadableChannels'
          )}
        </div>
      )}

      {Object.entries(groupBy(shown, (c) => c.server)).map(
        ([server, items]) => (
          <div key={server} className={styles.server}>
            <div className={styles.serverName}>{server}</div>
            <div className={styles.columns}>
              {items.map((channel) => (
                <Channel
                  key={channel.channelId}
                  channel={channel}
                  progress={progress[channel.channelId]}
                  isReading={reading.includes(channel.channelId)}
                  isDisabled={isDisabled}
                />
              ))}
            </div>
          </div>
        )
      )}

      {channels.length > 0 && (
        <Link className={styles.toggle} onPress={onTogglePress}>
          {showAll
            ? translate('ImportShowRequestrrChannels')
            : translate('ImportShowAllChannels', { count: channels.length })}
        </Link>
      )}
    </>
  );
}

export default ImportChannels;
