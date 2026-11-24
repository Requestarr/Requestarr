import React, { useCallback, useState } from 'react';
import Alert from 'Components/Alert';
import NotFound from 'Components/NotFound';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import PageSectionContent from 'Components/Page/PageSectionContent';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { plainText } from '../../portalText';
import { OnboardResult, PortalServerMember } from '../../PortalTypes';
import usePortal from '../../usePortal';
import OnboardProgress from './OnboardProgress';
import PickPeople from './PickPeople';
import SetUpPeople from './SetUpPeople';
import useStartRun, { NO_SETUP, OnboardSetup } from './useStartRun';
import styles from '../../Portal.css';

// Pick, set up, then watch it happen; Retry starts a new run with the same setup for those who failed.
function Steps({ people }: { people: PortalServerMember[] }) {
  const [picked, setPicked] = useState<PortalServerMember[]>();
  // Who was picked before Back, picked again on the list.
  const [kept, setKept] = useState<string[]>([]);
  const [setup, setSetup] = useState<OnboardSetup>(NO_SETUP);
  const { runId, refused, isStarting, start } = useStartRun();
  const onBack = useCallback(() => {
    setKept((picked ?? []).map((person) => person.discordId));
    setPicked(undefined);
  }, [picked]);
  const onConfirm = useCallback(
    (chosen: OnboardSetup) => {
      setSetup(chosen);
      start(picked ?? [], chosen);
    },
    [picked, start]
  );
  const onRetry = useCallback(
    (failed: OnboardResult[]) => start(failed, setup),
    [setup, start]
  );

  if (runId) {
    return (
      <>
        {refused && (
          <Alert kind={kinds.DANGER}>{plainText(refused.message)}</Alert>
        )}
        <OnboardProgress
          key={runId}
          runId={runId}
          isStarting={isStarting}
          onRetry={onRetry}
        />
      </>
    );
  }

  if (picked) {
    return (
      <>
        {refused && (
          <Alert kind={kinds.DANGER}>{plainText(refused.message)}</Alert>
        )}
        <SetUpPeople
          people={picked}
          isStarting={isStarting}
          onBack={onBack}
          onConfirm={onConfirm}
        />
      </>
    );
  }

  return people.length ? (
    <PickPeople people={people} kept={kept} onNext={setPicked} />
  ) : (
    <div className={styles.empty}>{translate('PortalNobodyToAdd')}</div>
  );
}

// Add member: everyone in the Discord server who isn't onboarded, to pick one or many, set up together and onboard,
// paced for Discord, with each person's progress.
function AddMembersPage() {
  const result = usePortal<PortalServerMember[]>('server-members');

  return (
    <PageContent title={translate('PortalActionOnboard')}>
      <PageContentBody>
        {result.isNotFound ? (
          <NotFound message={translate('PortalNotFound')} />
        ) : (
          <PageSectionContent
            isFetching={result.isLoading}
            isPopulated={result.data !== undefined}
            error={result.hasError ? result : undefined}
            errorMessage={translate('PortalLoadError')}
          >
            {result.data !== undefined && <Steps people={result.data} />}
          </PageSectionContent>
        )}
      </PageContentBody>
    </PageContent>
  );
}

export default AddMembersPage;
