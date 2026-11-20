import React, { useCallback, useMemo } from 'react';
import { useSelect } from 'App/SelectContext';
import translate from 'Utilities/String/translate';
import getSelectedIds from 'Utilities/Table/getSelectedIds';
import SelectFooter from '../Components/Select/SelectFooter';
import { PortalMember } from '../PortalTypes';
import { actionLabel } from '../Requests/ActionButton';
import { BULK_MEMBER_ACTIONS, memberActions } from './memberActionRules';

interface MembersSelectFooterProps {
  members: PortalMember[];
  isBusy: boolean;

  // Asks first, naming everyone picked (or opens Change access), then does it to each.
  onAsk: (members: PortalMember[], action: string) => void;
}

// The bar along the bottom in select mode, as Prowlarr's IndexerIndexSelectFooter: suspend, unsuspend and change
// access for every member picked, when every one of them allows it, and how many are picked.
function MembersSelectFooter({
  members,
  isBusy,
  onAsk,
}: MembersSelectFooterProps) {
  const [selectState] = useSelect();
  const picked = useMemo(() => {
    const ids = getSelectedIds(selectState.selectedState);

    return members.filter((member) => ids.includes(member.id));
  }, [members, selectState.selectedState]);

  const actions = useMemo(
    () =>
      BULK_MEMBER_ACTIONS.map((name) => ({
        name,
        label: actionLabel(name),
        isDanger: false,
        isOffered:
          picked.length > 0 &&
          picked.every((member) => memberActions(member).includes(name)),
      })),
    [picked]
  );

  const onAction = useCallback(
    (name: string) => onAsk(picked, name),
    [onAsk, picked]
  );

  return (
    <SelectFooter
      actions={actions}
      isBusy={isBusy}
      selectedText={translate('PortalCountMembersSelected', {
        count: picked.length,
      })}
      onAction={onAction}
    />
  );
}

export default MembersSelectFooter;
