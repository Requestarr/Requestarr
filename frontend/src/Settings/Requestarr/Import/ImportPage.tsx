import React, { useCallback, useMemo, useState } from 'react';
import { useDispatch, useSelector } from 'react-redux';
import AppState from 'App/State/AppState';
import Alert from 'Components/Alert';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import { setImportPreviewTableOption } from 'Store/Actions/importPreviewActions';
import translate from 'Utilities/String/translate';
import useRequestarrApi from '../useRequestarrApi';
import ImportAccessNotes from './ImportAccessNotes';
import ImportChannels from './ImportChannels';
import ImportPreview from './ImportPreview';
import ImportToolbar from './ImportToolbar';
import { ImportAccess, ImportChannel, ImportJob } from './importTypes';
import useChannelChecks, { channelIdsIn } from './useChannelChecks';
import useImportJob from './useImportJob';
import styles from './ImportPage.css';

function Progress({ job }: { job: ImportJob }) {
  return (
    <Alert kind={kinds.INFO}>
      {job.channels
        .map((c) =>
          translate('ImportReading', {
            channel: c.name ?? c.channelId,
            read: c.read,
          })
        )
        .join(' · ')}
    </Alert>
  );
}

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

// The channels typed in, whether every one can be read, and the preview job: what the toolbar's buttons act on.
function useImport() {
  const [text, setText] = useState('');
  const ids = useMemo(() => channelIdsIn(text), [text]);
  const checks = useChannelChecks(ids);
  const importJob = useImportJob();
  const { start, isBusy, job } = importJob;
  const onPreview = useCallback(() => start(ids), [ids, start]);
  const canPreview =
    !isBusy &&
    ids.length > 0 &&
    checks.length === ids.length &&
    checks.every((c) => c.channel?.canRead);

  return {
    text,
    onTextChange: setText,
    checks,
    canPreview,
    onPreview,
    job,
    error: importJob.error,
    onImport: importJob.confirm,
    isReading: job?.state === 'reading',
    isImporting: job?.state === 'importing',
    canImport: !isBusy && job?.state === 'ready',
  };
}

// Import → From Requestrr: the old request channels by ID, a preview of everything it would import, then Import in
// the toolbar, which spins while the import's command writes it (System, then Tasks). Discord is only read; nothing
// is posted, edited or reacted to there.
function ImportPage() {
  const page = useImport();
  const { job, error } = page;
  const { data: access } = useRequestarrApi<ImportAccess>('import/access');
  const { data: readable } =
    useRequestarrApi<ImportChannel[]>('import/channels');
  const columns = useSelector((state: AppState) => state.importPreview.columns);
  const dispatch = useDispatch();
  const onTableOptionChange = useCallback(
    (payload: unknown) => dispatch(setImportPreviewTableOption(payload)),
    [dispatch]
  );

  return (
    <PageContent title={translate('ImportFromRequestrr')}>
      <ImportToolbar
        columns={columns}
        canPreview={page.canPreview}
        isReading={page.isReading}
        canImport={page.canImport}
        isImporting={page.isImporting}
        onPreviewPress={page.onPreview}
        onImportPress={page.onImport}
        onTableOptionChange={onTableOptionChange}
      />

      <PageContentBody>
        <div className={styles.intro}>{translate('ImportIntro')}</div>
        <ImportAccessNotes access={access} />
        <ImportChannels
          text={page.text}
          checks={page.checks}
          readable={readable}
          onChange={page.onTextChange}
        />

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
        {page.isReading && job && <Progress job={job} />}
        {job && <Outcome job={job} />}
        {job?.warning && <Alert kind={kinds.WARNING}>{job.warning}</Alert>}
        {job?.plan && <ImportPreview plan={job.plan} />}
      </PageContentBody>
    </PageContent>
  );
}

export default ImportPage;
