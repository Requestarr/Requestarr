using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Access;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Imports;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // Someone in the Discord server who isn't a member yet, for Add member's list: their picture, when they joined, their
    // roles there, and how many requests an import brought for them (0 for none).
    public record PortalServerMember(string DiscordId, string Name, string Username, string Avatar, DateTime? Joined, IReadOnlyList<string> Roles,
                                     int ImportedRequests);

    public interface IPortalMemberActions
    {
        // Everyone in the server who isn't onboarded (bots left out), those with imported history first; null while the bot
        // isn't connected, since then nobody can be listed.
        Task<IReadOnlyList<PortalServerMember>> ServerMembersAsync();

        ServiceResult SetAccess(int memberId, ContentType type, AccessState state, Actor admin);

        // Limit 0: no limit.
        ServiceResult SetLimit(int memberId, ContentType type, int limit, int days, Actor admin);
        Task<ServiceResult> SuspendAsync(int memberId, Actor admin);
        Task<ServiceResult> UnsuspendAsync(int memberId, Actor admin);
        Task<ServiceResult> RemoveAsync(int memberId, Actor admin);
        ServiceResult LinkPlex(int memberId, string username, Actor admin);

        // Their imported requests in their own channel, once.
        ServiceResult PostHistory(int memberId, Actor admin);
        Task<ServiceResult> DecideMediaLinkAsync(int linkRequestId, bool approve, Actor admin);

        Task<ServiceResult> ApproveAccessAsync(int accessRequestId, IDictionary<ContentType, AccessState> levels, Actor admin);
        Task<ServiceResult> DenyAccessAsync(int accessRequestId, string reason, Actor admin);
        Task<ServiceResult> UnblockAsync(string discordId, Actor admin);
    }

    // What an admin does to members and to people asking to join, in the portal: the same services as the member card,
    // the access request cards and the paused notice in Discord. Those take the person's Discord account, which the
    // portal's member pages find from the member.
    public class PortalMemberActions : IPortalMemberActions
    {
        // Every member is onboarded with their Discord account; this only answers a stale or hand-made address.
        private static readonly ServiceResult NoDiscord = ServiceResult.Fail("That member has no Discord account in Requestarr.");

        private readonly IUserManagementService _users;
        private readonly IAccessRequestService _accessRequests;
        private readonly IUserIdentityRepository _identities;
        private readonly IRequestarrUserRepository _members;
        private readonly IRequestRepository _requests;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IHistoryPosting _history;

        public PortalMemberActions(IUserManagementService users,
                                   IAccessRequestService accessRequests,
                                   IUserIdentityRepository identities,
                                   IRequestarrUserRepository members,
                                   IRequestRepository requests,
                                   IDiscordGuildGateway gateway,
                                   IHistoryPosting history)
        {
            _users = users;
            _accessRequests = accessRequests;
            _identities = identities;
            _members = members;
            _requests = requests;
            _gateway = gateway;
            _history = history;
        }

        public async Task<IReadOnlyList<PortalServerMember>> ServerMembersAsync()
        {
            if (!_gateway.IsConnected)
            {
                return null;
            }

            // Someone known only from an import is listed, with their history, to be onboarded. Onboarded means they have
            // their channel, as onboarding decides: an admin's own record, or one made when they asked for a Plex link, has none.
            var people = _members.All().ToDictionary(u => u.Id);
            var discordOf = _identities.All().Where(i => i.Provider == UserIdentity.DiscordProvider && people.ContainsKey(i.UserId)).ToDictionary(i => i.UserId, i => i.ExternalId);
            var onboarded = people.Values.Where(u => u.Status != UserStatus.Removed && u.PrivateChannelId != null).Select(u => discordOf.GetValueOrDefault(u.Id)).ToHashSet();
            var imported = _requests.All().Where(r => people.GetValueOrDefault(r.OwnerUserId)?.IsImported == true && discordOf.ContainsKey(r.OwnerUserId))
                .GroupBy(r => discordOf[r.OwnerUserId]).ToDictionary(g => g.Key, g => g.Count());
            var roles = await _gateway.GetRoleNamesAsync();
            var everyone = await _gateway.GetMembersAsync();

            return everyone.Where(m => !m.IsBot && !onboarded.Contains(m.Id.ToString()))
                .Select(m => new PortalServerMember(
                    m.Id.ToString(),
                    m.DisplayName,
                    m.Username,
                    m.AvatarUrl,
                    m.JoinedAt?.UtcDateTime,
                    m.RoleIds.Select(r => roles.GetValueOrDefault(r)).Where(n => n != null).ToList(),
                    imported.GetValueOrDefault(m.Id.ToString())))
                .OrderByDescending(p => p.ImportedRequests > 0)
                .ThenBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public ServiceResult SetAccess(int memberId, ContentType type, AccessState state, Actor admin) =>
            WithDiscord(memberId, id => _users.SetAccess(id, type, state, admin));

        public ServiceResult SetLimit(int memberId, ContentType type, int limit, int days, Actor admin) =>
            WithDiscord(memberId, id => _users.SetQuota(id, type, limit, days, admin));

        public Task<ServiceResult> SuspendAsync(int memberId, Actor admin) => WithDiscordAsync(memberId, id => _users.SuspendAsync(id, admin));

        public Task<ServiceResult> UnsuspendAsync(int memberId, Actor admin) => WithDiscordAsync(memberId, id => _users.UnsuspendAsync(id, admin));

        public Task<ServiceResult> RemoveAsync(int memberId, Actor admin) => WithDiscordAsync(memberId, id => _users.RemoveAsync(id, admin));

        public ServiceResult LinkPlex(int memberId, string username, Actor admin) =>
            WithDiscord(memberId, id => _users.LinkMediaServer(id, MediaServerProvider.Plex, username, admin));

        public ServiceResult PostHistory(int memberId, Actor admin) => _history.PostMember(memberId, admin);

        public Task<ServiceResult> DecideMediaLinkAsync(int linkRequestId, bool approve, Actor admin) =>
            _users.DecideAccessRequestAsync(linkRequestId, approve, admin, PortalActions.NameOf(admin));

        public Task<ServiceResult> ApproveAccessAsync(int accessRequestId, IDictionary<ContentType, AccessState> levels, Actor admin) =>
            _accessRequests.ApproveAsync(accessRequestId, levels, admin, PortalActions.NameOf(admin));

        public Task<ServiceResult> DenyAccessAsync(int accessRequestId, string reason, Actor admin) =>
            _accessRequests.DenyAsync(accessRequestId, reason, admin, PortalActions.NameOf(admin));

        public Task<ServiceResult> UnblockAsync(string discordId, Actor admin) =>
            ulong.TryParse(discordId, out var id) ? _accessRequests.UnblockAsync(id, admin) : Task.FromResult(ServiceResult.Fail("That person isn't paused."));

        private ulong? DiscordId(int memberId) =>
            ulong.TryParse(_identities.GetForUser(memberId).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId, out var id) ? id : null;

        private ServiceResult WithDiscord(int memberId, Func<ulong, ServiceResult> act) => DiscordId(memberId) is { } id ? act(id) : NoDiscord;

        private Task<ServiceResult> WithDiscordAsync(int memberId, Func<ulong, Task<ServiceResult>> act) =>
            DiscordId(memberId) is { } id ? act(id) : Task.FromResult(NoDiscord);
    }
}
