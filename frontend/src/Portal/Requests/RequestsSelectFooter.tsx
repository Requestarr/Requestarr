import React from 'react';
import translate from 'Utilities/String/translate';
import SelectFooter from '../Components/Select/SelectFooter';
import { PortalRequest } from '../PortalTypes';
import useRequestBulk from './useRequestBulk';

interface RequestsSelectFooterProps {
  requests: PortalRequest[];
  onDone: () => void;
}

// The bar along the bottom in select mode: what can be done to every request picked, and how many are picked.
function RequestsSelectFooter({ requests, onDone }: RequestsSelectFooterProps) {
  const bulk = useRequestBulk(requests, onDone);

  return (
    <>
      <SelectFooter
        actions={bulk.actions}
        isBusy={bulk.isBusy}
        selectedText={translate('PortalCountRequestsSelected', {
          count: bulk.picked.length,
        })}
        onAction={bulk.onAction}
      />
      {bulk.modal}
    </>
  );
}

export default RequestsSelectFooter;
