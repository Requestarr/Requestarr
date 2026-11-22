import React, { useCallback, useEffect, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
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
import useRequestarrApi from '../useRequestarrApi';
import ImportAccessNotes from './ImportAccessNotes';
import ImportChannels, { PickableChannel } from './ImportChannels';
import ImportPreview from './ImportPreview';
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
          </>
        }
        position={tooltipPositions.RIGHT}
      />
    </>
  );
}

// The channels the bot can read, numbered for the SelectProvider.
function usePickable(readable?: ImportChannel[]): PickableChannel[] {
  return useMemo(
    () =>
      (readable ?? [])
        .filter((c) => c.canRead)
        .map((c, index) => ({
          id: index,
          channelId: c.id,
          server: c.server ?? '',
          name: c.name ?? c.id,
        })),
    [readable]
  );
}

// The picked channels' IDs: a new preview a moment after the picks change, or none when nothing is picked.
function usePickedPreview(channels: PickableChannel[]) {
  const [selectState] = useSelect();
  const importJob = useImportJob();
  const { start, reset } = importJob;
  const key = getSelectedIds(selectState.selectedState)
    .map((index) => channels[index]?.channelId)
    .filter(Boolean)
    .join(',');

  useEffect(() => {
    const ids = key ? key.split(',') : [];
    const timer = setTimeout(
      () => (ids.length ? start(ids) : reset()),
      PICK_DELAY_MILLISECONDS
    );

    return () => clearTimeout(timer);
  }, [key, start, reset]);

  return importJob;
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
  channels: PickableChannel[];
}

// Pick the old Requestrr channels; the list of what an import would bring in follows the picks, then Import in the
// toolbar writes it (a task in System, then Tasks). Discord is only read; nothing is posted, edited or reacted to there.
function ImportContent({ access, channels }: ImportContentProps) {
  const { job, error, isBusy, confirm } = usePickedPreview(channels);
  const progress = useProgress(job);
  const { columns, onTableOptionChange } = useTableOptions();
  const isImporting = job?.state === 'importing';
  const inviteUrl = access?.readInviteUrl;
  const onInvitePress = useCallback(
    () => window.open(inviteUrl, '_blank', 'noopener'),
    [inviteUrl]
  );

  return (
    <PageContent title={translate('ImportFromRequestrr')}>
      <ImportToolbar
        columns={columns}
        inviteUrl={inviteUrl}
        canImport={!isBusy && job?.state === 'ready'}
        isImporting={isImporting}
        onImportPress={confirm}
        onInvitePress={onInvitePress}
        onTableOptionChange={onTableOptionChange}
      />

      <PageContentBody>
        <ImportAccessNotes access={access} />

        <FieldSet legend={<ChannelsLegend />}>
          <ImportChannels
            channels={channels}
            progress={progress}
            isDisabled={isImporting}
          />
        </FieldSet>

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
        {job && <Outcome job={job} />}
        {job?.warning && <Alert kind={kinds.WARNING}>{job.warning}</Alert>}
        {job?.plan && <ImportPreview plan={job.plan} />}
      </PageContentBody>
    </PageContent>
  );
}

function ImportPage() {
  const { data: access } = useRequestarrApi<ImportAccess>('import/access');
  const { data: readable } =
    useRequestarrApi<ImportChannel[]>('import/channels');
  const channels = usePickable(readable);

  return (
    <SelectProvider items={channels}>
      <ImportContent access={access} channels={channels} />
    </SelectProvider>
  );
}

export default ImportPage;
