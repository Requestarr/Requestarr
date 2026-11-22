import { groupBy } from 'lodash';
import React, { useCallback } from 'react';
import { useSelect } from 'App/SelectContext';
import Icon from 'Components/Icon';
import TableRowCell from 'Components/Table/Cells/TableRowCell';
import TableSelectCell from 'Components/Table/Cells/TableSelectCell';
import Table from 'Components/Table/Table';
import TableBody from 'Components/Table/TableBody';
import TableRow from 'Components/Table/TableRow';
import { icons } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { ImportProgress } from './importTypes';
import styles from './ImportPage.css';

// A channel the bot can read, numbered for the page's SelectProvider (Discord's IDs are too long for its numbers).
export interface PickableChannel {
  id: number;
  channelId: string;
  server: string;
  name: string;
}

const columns = [
  { name: 'select', isVisible: true },
  { name: 'channel', label: () => translate('ImportChannel'), isVisible: true },
  {
    name: 'read',
    label: () => translate('ImportMessagesRead'),
    isVisible: true,
  },
];

interface ChannelRowProps {
  channel: PickableChannel;
  progress?: ImportProgress;
  isDisabled: boolean;
}

interface SelectedChange {
  value: boolean | null;
  shiftKey?: boolean;
}

// A channel with its box, as Sonarr's rows are picked, and how many of its messages have been read.
function ChannelRow({ channel, progress, isDisabled }: ChannelRowProps) {
  const [selectState, selectDispatch] = useSelect();
  const onSelectedChange = useCallback(
    ({ value, shiftKey = false }: SelectedChange) =>
      selectDispatch({
        type: 'toggleSelected',
        id: channel.id,
        isSelected: !!value,
        shiftKey,
      }),
    [channel, selectDispatch]
  );

  return (
    <TableRow>
      <TableSelectCell
        id={channel.id}
        isSelected={!!selectState.selectedState[channel.id]}
        isDisabled={isDisabled}
        onSelectedChange={onSelectedChange}
      />
      <TableRowCell>{`#${channel.name}`}</TableRowCell>
      <TableRowCell>
        {progress?.read.toLocaleString()}
        {progress && !progress.done && (
          <Icon
            className={styles.reading}
            name={icons.SPINNER}
            isSpinning={true}
          />
        )}
      </TableRowCell>
    </TableRow>
  );
}

interface ImportChannelsProps {
  channels: PickableChannel[];
  progress: Record<string, ImportProgress>;
  isDisabled: boolean;
}

// Every channel the bot can read, under its server's name: ticking one reads it (once; Requestarr keeps what it read).
function ImportChannels({
  channels,
  progress,
  isDisabled,
}: ImportChannelsProps) {
  const servers = groupBy(channels, (c) => c.server);

  if (!channels.length) {
    return (
      <div className={styles.note}>{translate('ImportNoReadableChannels')}</div>
    );
  }

  return (
    <>
      {Object.entries(servers).map(([server, rows]) => (
        <div key={server} className={styles.server}>
          <div className={styles.serverName}>{server}</div>
          <Table columns={columns}>
            <TableBody>
              {rows.map((channel) => (
                <ChannelRow
                  key={channel.channelId}
                  channel={channel}
                  progress={progress[channel.channelId]}
                  isDisabled={isDisabled}
                />
              ))}
            </TableBody>
          </Table>
        </div>
      ))}
    </>
  );
}

export default ImportChannels;
