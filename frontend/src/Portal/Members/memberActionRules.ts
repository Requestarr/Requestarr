import { PortalMember } from '../PortalTypes';

// What an admin can do to a member from the Members list, by their status, as their page and Discord's member card
// offer it: an onboarded member's access, suspend or unsuspend, and remove; an active one with imported requests, posting
// them in their channel; someone imported who's in the server, or removed, onboarding. Someone who left the server has
// nothing here.
export function memberActions(member: PortalMember): string[] {
  switch (member.status) {
    case 'active':
      return [
        'changeAccess',
        'suspend',
        ...(member.importedRequests > 0 ? ['post-history'] : []),
        'remove-member',
      ];
    case 'suspended':
      return ['changeAccess', 'unsuspend', 'remove-member'];
    case 'imported':
    case 'removed':
      return ['onboard'];
    default:
      return [];
  }
}

// What the toolbar offers in select mode, for one member or several (Onboard is a button of its own).
export const GROUP_MEMBER_ACTIONS = [
  'suspend',
  'unsuspend',
  'changeAccess',
  'post-history',
  'remove-member',
];
