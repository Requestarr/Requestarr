import React, { useCallback } from 'react';
import Alert from 'Components/Alert';
import FormGroup from 'Components/Form/FormGroup';
import FormInputGroup from 'Components/Form/FormInputGroup';
import FormLabel from 'Components/Form/FormLabel';
import Button from 'Components/Link/Button';
import { inputTypes, kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { ImportChannel } from './importTypes';
import { ChannelCheck } from './useChannelChecks';
import styles from './ImportPage.css';

interface ImportChannelsProps {
  text: string;
  checks: ChannelCheck[];
  readable?: ImportChannel[];
  onChange: (text: string) => void;
}

function CheckLine({ check }: { check: ChannelCheck }) {
  const { channel } = check;
  const kind = channel?.canRead ? kinds.SUCCESS : kinds.DANGER;
  const problem = check.problem || channel?.problem;
  const where = channel?.name
    ? translate('ImportChannelFound', {
        channel: channel.name,
        server: channel.server ?? '',
      })
    : check.id;

  return (
    <Alert kind={kind}>
      {where}
      {channel?.canRead ? ` · ${translate('ImportChannelReadable')}` : ''}
      {problem ? ` · ${problem}` : ''}
    </Alert>
  );
}

function ChannelButton({
  channel,
  onAdd,
}: {
  channel: ImportChannel;
  onAdd: (id: string) => void;
}) {
  const onPress = useCallback(() => onAdd(channel.id), [channel, onAdd]);

  return (
    <Button onPress={onPress}>{`#${channel.name} · ${channel.server}`}</Button>
  );
}

// The old request channels, by ID: each checked as it's entered, with the channels the bot can read as a shortcut.
function ImportChannels({
  text,
  checks,
  readable,
  onChange,
}: ImportChannelsProps) {
  const onTextChange = useCallback(
    ({ value }: { value: string }) => onChange(value),
    [onChange]
  );
  const add = useCallback(
    (id: string) => {
      const current = text.trim();
      onChange(current ? `${current}\n${id}` : id);
    },
    [onChange, text]
  );

  return (
    <>
      <FormGroup>
        <FormLabel>{translate('ImportChannelIds')}</FormLabel>
        <FormInputGroup
          type={inputTypes.TEXT_AREA}
          name="channelIds"
          value={text}
          helpText={translate('ImportChannelIdsHelpText')}
          onChange={onTextChange}
        />
      </FormGroup>

      {checks.map((check) => (
        <CheckLine key={check.id} check={check} />
      ))}

      {readable && readable.length > 0 && (
        <div className={styles.readable}>
          <div>{translate('ImportReadableChannels')}</div>
          {readable.map((channel) => (
            <ChannelButton key={channel.id} channel={channel} onAdd={add} />
          ))}
        </div>
      )}
    </>
  );
}

export default ImportChannels;
