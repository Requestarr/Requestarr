import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import { useLocation } from 'react-router-dom';
import { SelectProvider, useSelect } from 'App/SelectContext';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import FieldSet from 'Components/FieldSet';
import Icon from 'Components/Icon';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import Popover from 'Components/Tooltip/Popover';
import { icons, kinds, tooltipPositions } from 'Helpers/Props';
import { setImportPreviewTableOption } from 'Store/Actions/importPreviewActions';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import AddServerModal from '../Connections/Discord/AddServerModal';
import { SetupStatus } from '../RequestarrSettingsTypes';
import useRequestarrApi from '../useRequestarrApi';
import useSetupStatus from '../useSetupStatus';
import ImportAccessNotes from './ImportAccessNotes';
import ImportChannels, { PickableChannel } from './ImportChannels';
import ImportPreview from './ImportPreview';
import ImportServers from './ImportServers';
import ImportToolbar from './ImportToolbar';
import {
  ImportAccess,
  ImportChannel,
  ImportJob,
  ImportProgress,
} from './importTypes';
import useImportJob from './useImportJob';
import styles from './ImportPage.css';

// Wait this long after the last tick before reading, so ticking several channels reads them in one preview.
const PICK_DELAY_MILLISECONDS = 400;

function Outcome({ job }: { job: ImportJob }) {
  if (job.state === 'failed') {
    return <Alert kind={kinds.DANGER}>{job.problem ?? ''}</Alert>;
  }

  return job.result ? (
    <Alert kind={kinds.SUCCESS}>
      {translate('ImportDone', { ...job.result })}
    </Alert>
  ) : null;
}

// What the page says about importing, behind the heading's info icon, as Sonarr explains its monitoring options.
function ChannelsLegend() {
  return (
    <>
      {translate('ImportChannels')}
      <Popover
        anchor={<Icon className={styles.help} name={icons.INFO} />}
        title={translate('ImportFromRequestrr')}
        body={
          <>
            <p>{translate('ImportIntro')}</p>
            <p>{translate('ImportReadInviteHelpText')}</p>
            <p>{translate('ImportPickHelpText')}</p>
          </>
        }
        position={tooltipPositions.RIGHT}
      />
    </>
  );
}

// The channels the bot can read (one server's, when its card was pressed), numbered from 1 for the SelectProvider
// (its shift-click range treats 0 as none).
function usePickable(
  readable?: ImportChannel[],
  server?: string
): PickableChannel[] {
  return useMemo(
    () =>
      (readable ?? [])
        .filter((c) => c.canRead && (!server || c.serverId === server))
        .map((c, index) => ({
          id: index + 1,
          channelId: c.id,
          server: c.server ?? '',
          name: c.name ?? c.id,
          hasSourceReplies: !!c.hasSourceReplies,
        })),
    [readable, server]
  );
}

// The picked channels' IDs: a new preview a moment after the picks change, or none when nothing is picked. Changed:
// the picks aren't yet what the preview shows, so Import waits rather than write the earlier picks.
function usePickedPreview(channels: PickableChannel[]) {
  const [selectState] = useSelect();
  const importJob = useImportJob();
  const { job, start, reset } = importJob;
  const key = getSelectedIds(selectState.selectedState)
    .map((id) => channels.find((c) => c.id === id)?.channelId)
    .filter(Boolean)
    .sort()
    .join(',');
  const shown = (job?.channels ?? [])
    .map((c) => c.channelId)
    .sort()
    .join(',');

  useEffect(() => {
    const ids = key ? key.split(',') : [];
    const timer = setTimeout(
      () => (ids.length ? start(ids) : reset()),
      PICK_DELAY_MILLISECONDS
    );

    return () => clearTimeout(timer);
  }, [key, start, reset]);

  return { ...importJob, isChanged: key !== shown };
}

// The channels the current preview is still reading: the only ones that show a spinner.
function readingIds(job?: ImportJob) {
  return job?.state === 'reading'
    ? job.channels.filter((c) => !c.done).map((c) => c.channelId)
    : [];
}

// How far each channel has been read, kept across previews so a channel unpicked still shows its count.
function useProgress(job?: ImportJob) {
  const [progress, setProgress] = useState<Record<string, ImportProgress>>({});
  const [seen, setSeen] = useState<ImportJob>();

  // Each new answer is added while rendering, as React suggests for state that follows a prop.
  if (job !== seen) {
    setSeen(job);
    setProgress((current) => ({
      ...current,
      ...Object.fromEntries((job?.channels ?? []).map((c) => [c.channelId, c])),
    }));
  }

  return progress;
}

function useTableOptions() {
  const columns = useSelector((state: AppState) => state.importPreview.columns);
  const dispatch = useDispatch();
  const onTableOptionChange = useCallback(
    (payload: unknown) => dispatch(setImportPreviewTableOption(payload)),
    [dispatch]
  );

  return { columns, onTableOptionChange };
}

interface ImportContentProps {
  access?: ImportAccess;
  setup?: SetupStatus;
  server?: string;
  channels: PickableChannel[];
  isLoading: boolean;
}

// Pick the old Requestrr channels; the list of what an import would bring in follows the picks, then Import in the
// toolbar writes it (a task in System, then Tasks). Discord is only read; nothing is posted, edited or reacted to
// there.
function ImportContent(props: ImportContentProps) {
  const { access, setup, server, channels, isLoading } = props;
  const { job, error, isBusy, isConfirming, isChanged, confirm } =
    usePickedPreview(channels);
  const progress = useProgress(job);
  const { columns, onTableOptionChange } = useTableOptions();
  const isImporting = job?.state === 'importing';
  const [isAdding, setIsAdding] = useState(false);
  const onInvitePress = useCallback(() => setIsAdding(true), []);
  const onAddClose = useCallback(() => setIsAdding(false), []);

  return (
    <PageContent title={translate('ImportFromRequestrr')}>
      <ImportToolbar
        columns={columns}
        canInvite={!!access?.readInviteUrl}
        canImport={!isBusy && !isChanged && job?.state === 'ready'}
        isImporting={isImporting}
        onImportPress={confirm}
        onInvitePress={onInvitePress}
        onTableOptionChange={onTableOptionChange}
      />

      <PageContentBody>
        <ImportAccessNotes access={access} />

        <ImportServers setup={setup} shown={server} onAdd={onInvitePress} />

        <FieldSet legend={<ChannelsLegend />}>
          <ImportChannels
            channels={channels}
            isLoading={isLoading}
            progress={progress}
            reading={readingIds(job)}
            isDisabled={isImporting || isConfirming}
          />
        </FieldSet>

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
        {job && <Outcome job={job} />}
        {job?.warning && <Alert kind={kinds.WARNING}>{job.warning}</Alert>}
        {job?.plan && <ImportPreview plan={job.plan} />}
      </PageContentBody>

      {setup && (
        <AddServerModal
          isOpen={isAdding}
          setup={setup}
          importOnly={true}
          onClose={onAddClose}
        />
      )}
    </PageContent>
  );
}

// The channels follow the bot's servers live: a server it joins for the import shows its channels without a refresh.
function ImportPage() {
  const { data: access } = useRequestarrApi<ImportAccess>('import/access');
  const { data: readable, reload } =
    useRequestarrApi<ImportChannel[]>('import/channels');
  const { data: setup } = useSetupStatus();
  const query = new URLSearchParams(useLocation().search);
  const server = query.get('server') ?? undefined;
  const channels = usePickable(readable, server);

  useEffect(reload, [setup, reload]);

  return (
    <SelectProvider items={channels}>
      <ImportContent
        access={access}
        setup={setup}
        server={server}
        channels={channels}
        isLoading={!readable}
      />
    </SelectProvider>
  );
}

export default ImportPage;
