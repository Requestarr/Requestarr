using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Access;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.MediaServers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // Someone in the Discord server who isn't a member yet, from Add member's search.
    public record PortalServerMember(string DiscordId, string Name, string Username);

    public interface IPortalMemberActions
    {
        Task<IReadOnlyList<PortalServerMember>> SearchServerAsync(string term);
        Task<ServiceResult> AddAsync(string discordId, Actor admin);

        // InstanceId: the member's own instance for the type; null leaves it as it is.
        ServiceResult SetAccess(int memberId, ContentType type, AccessState state, int? instanceId, Actor admin);

        // Limit 0: no limit.
        ServiceResult SetLimit(int memberId, ContentType type, int limit, int days, Actor admin);
        Task<ServiceResult> SuspendAsync(int memberId, Actor admin);
        Task<ServiceResult> UnsuspendAsync(int memberId, Actor admin);
        Task<ServiceResult> RemoveAsync(int memberId, Actor admin);
        ServiceResult LinkPlex(int memberId, string username, Actor admin);
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
        // As many matches as Discord's member picker lists.
        private const int SearchLimit = 25;

        // Every member is onboarded with their Discord account; this only answers a stale or hand-made address.
        private static readonly ServiceResult NoDiscord = ServiceResult.Fail("That member has no Discord account in Requestarr.");

        private readonly IUserManagementService _users;
        private readonly IAccessRequestService _accessRequests;
        private readonly IUserIdentityRepository _identities;
        private readonly IRequestarrUserRepository _members;
        private readonly IBackendRepository _backends;
        private readonly IDiscordGuildGateway _gateway;

        public PortalMemberActions(IUserManagementService users,
                                   IAccessRequestService accessRequests,
                                   IUserIdentityRepository identities,
                                   IRequestarrUserRepository members,
                                   IBackendRepository backends,
                                   IDiscordGuildGateway gateway)
        {
            _users = users;
            _accessRequests = accessRequests;
            _identities = identities;
            _members = members;
            _backends = backends;
            _gateway = gateway;
        }

        public async Task<IReadOnlyList<PortalServerMember>> SearchServerAsync(string term)
        {
            if (string.IsNullOrWhiteSpace(term) || !_gateway.IsConnected)
            {
                return new List<PortalServerMember>();
            }

            // Someone known only from an import can still be found and onboarded.
            var active = _members.All().Where(u => u.Status is UserStatus.Active or UserStatus.Suspended).Select(u => DiscordId(u.Id)).ToHashSet();
            var found = await _gateway.SearchMembersAsync(term.Trim(), SearchLimit);

            return found.Where(m => !m.IsBot && !active.Contains(m.Id))
                .Select(m => new PortalServerMember(m.Id.ToString(), m.DisplayName, m.Username))
                .ToList();
        }

        public Task<ServiceResult> AddAsync(string discordId, Actor admin) =>
            ulong.TryParse(discordId, out var id)
                ? _users.AddAsync(id, new Dictionary<ContentType, AccessState>(), admin)
                : Task.FromResult(ServiceResult.Fail("Pick someone from the server."));

        public ServiceResult SetAccess(int memberId, ContentType type, AccessState state, int? instanceId, Actor admin)
        {
            var instance = instanceId.HasValue ? _backends.Find(instanceId.Value) : null;

            if (instanceId.HasValue && instance == null)
            {
                return ServiceResult.Fail("That instance no longer exists.");
            }

            return WithDiscord(memberId, id => _users.SetAccess(id, type, state, admin, instance?.Name));
        }

        public ServiceResult SetLimit(int memberId, ContentType type, int limit, int days, Actor admin) =>
            WithDiscord(memberId, id => _users.SetQuota(id, type, limit, days, admin));

        public Task<ServiceResult> SuspendAsync(int memberId, Actor admin) => WithDiscordAsync(memberId, id => _users.SuspendAsync(id, admin));

        public Task<ServiceResult> UnsuspendAsync(int memberId, Actor admin) => WithDiscordAsync(memberId, id => _users.UnsuspendAsync(id, admin));

        public Task<ServiceResult> RemoveAsync(int memberId, Actor admin) => WithDiscordAsync(memberId, id => _users.RemoveAsync(id, admin));

        public ServiceResult LinkPlex(int memberId, string username, Actor admin) =>
            WithDiscord(memberId, id => _users.LinkMediaServer(id, MediaServerProvider.Plex, username, admin));

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
