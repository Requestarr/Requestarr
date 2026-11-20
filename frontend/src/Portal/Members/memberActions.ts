import { PortalMember } from '../PortalTypes';

// What an admin can do to a member from the Members list, by their status, as their page and Discord's member card
// offer it: an onboarded member's access, suspend or unsuspend, and remove; someone imported who's in the server,
// onboarding. Someone who left the server or was removed has nothing here.
export function memberActions(member: PortalMember): string[] {
  switch (member.status) {
    case 'active':
      return ['changeAccess', 'suspend', 'remove-member'];
    case 'suspended':
      return ['changeAccess', 'unsuspend', 'remove-member'];
    case 'imported':
      return ['onboard'];
    default:
      return [];
  }
}

// The action a row shows as its own button, the one most likely wanted.
export function likelyMemberAction(member: PortalMember) {
  switch (member.status) {
    case 'imported':
      return 'onboard';
    case 'suspended':
      return 'unsuspend';
    case 'active':
      return 'changeAccess';
    default:
      return undefined;
  }
}

// What the selection bar offers for the members picked.
export const BULK_MEMBER_ACTIONS = ['suspend', 'unsuspend', 'changeAccess'];
