import React, { ReactNode } from 'react';
import Alert from 'Components/Alert';
import LoadingIndicator from 'Components/Loading/LoadingIndicator';
import PageContent from 'Components/Page/PageContent';
import PageContentBody from 'Components/Page/PageContentBody';
import { kinds } from 'Helpers/Props';
import translate from 'Utilities/String/translate';
import { PortalResult } from '../usePortal';

interface PortalPageProps<T> {
  title: string;
  result: PortalResult<T>;
  toolbar?: ReactNode;
  children: (data: T) => ReactNode;
}

// A portal page: its title and toolbar, then loading, "not found", an error, or the page's content once its view has arrived.
function PortalPage<T>({
  title,
  result,
  toolbar,
  children,
}: PortalPageProps<T>) {
  const { data, isLoading, isNotFound, hasError } = result;

  return (
    <PageContent title={title}>
      {toolbar}

      <PageContentBody>
        {isLoading && <LoadingIndicator />}

        {isNotFound && (
          <Alert kind={kinds.WARNING}>{translate('PortalNotFound')}</Alert>
        )}

        {hasError && (
          <Alert kind={kinds.DANGER}>{translate('PortalLoadError')}</Alert>
        )}

        {data !== undefined && children(data)}
      </PageContentBody>
    </PageContent>
  );
}

export default PortalPage;
