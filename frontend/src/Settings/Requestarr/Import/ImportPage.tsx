import React, { useCallback, useMemo, useState } from 'react';
import Alert from 'Components/Alert';
import SpinnerButton from 'Components/Link/SpinnerButton';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import useRequestarrApi from '../useRequestarrApi';
import ImportAccessNotes from './ImportAccessNotes';
import ImportChannels from './ImportChannels';
import ImportPreview from './ImportPreview';
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

// Import → From Requestrr: the old request channels by ID, a preview of everything it would import, then Confirm.
// Confirm, once the preview is ready; it spins while the import's command writes it (System, then Tasks).
function ConfirmButton({
  job,
  isBusy,
  onConfirm,
}: {
  job: ImportJob;
  isBusy: boolean;
  onConfirm: () => void;
}) {
  if (job.state !== 'ready' && job.state !== 'importing') {
    return null;
  }

  return (
    <SpinnerButton
      kind={kinds.SUCCESS}
      isSpinning={job.state === 'importing'}
      isDisabled={isBusy}
      onPress={onConfirm}
    >
      {translate('ImportConfirm')}
    </SpinnerButton>
  );
}

// Discord is only read; nothing is posted, edited or reacted to there.
function ImportPage() {
  const [text, setText] = useState('');
  const ids = useMemo(() => channelIdsIn(text), [text]);
  const checks = useChannelChecks(ids);
  const { data: access } = useRequestarrApi<ImportAccess>('import/access');
  const { data: readable } =
    useRequestarrApi<ImportChannel[]>('import/channels');
  const { job, error, isBusy, start, confirm } = useImportJob();
  const onPreview = useCallback(() => start(ids), [ids, start]);
  const canPreview =
    ids.length > 0 &&
    checks.length === ids.length &&
    checks.every((c) => c.channel?.canRead);

  return (
    <PageContent title={translate('ImportFromRequestrr')}>
      <PageContentBody>
        <div className={styles.intro}>{translate('ImportIntro')}</div>
        <ImportAccessNotes access={access} />
        <ImportChannels
          text={text}
          checks={checks}
          readable={readable}
          onChange={setText}
        />

        <SpinnerButton
          kind={kinds.PRIMARY}
          isSpinning={isBusy || job?.state === 'reading'}
          isDisabled={!canPreview}
          onPress={onPreview}
        >
          {translate('ImportPreview')}
        </SpinnerButton>

        {error && <Alert kind={kinds.DANGER}>{error}</Alert>}
        {job?.state === 'reading' && <Progress job={job} />}
        {job && <Outcome job={job} />}
        {job?.warning && <Alert kind={kinds.WARNING}>{job.warning}</Alert>}
        {job?.plan && <ImportPreview plan={job.plan} />}

        {job && <ConfirmButton job={job} isBusy={isBusy} onConfirm={confirm} />}
      </PageContentBody>
    </PageContent>
  );
}

export default ImportPage;
