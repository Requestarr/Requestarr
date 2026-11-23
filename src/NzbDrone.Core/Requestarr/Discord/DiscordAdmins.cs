using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Discord
{
    // Someone in Requestarr's server, for picking admins in the web app: whether they hold the admin role, and whether
    // they own the server (always an admin). The ID is text, since JavaScript can't hold it as a number.
    public record DiscordAdminCandidate(string DiscordId, string Name, string Username, string Avatar, bool IsAdmin, bool IsOwner);

    public record DiscordAdminsResult(bool Saved, string Problem);

    public interface IDiscordAdmins
    {
        Task<IReadOnlyList<DiscordAdminCandidate>> ListAsync();

        // Gives the admin role to the people named and takes it from everyone else who has it, except the server's
        // owner, who always keeps it.
        Task<DiscordAdminsResult> SetAsync(IReadOnlyCollection<ulong> adminIds, Actor actor);

        // The server's owner always holds the admin role. A provisioning file (dev and QA) names the admins itself.
        Task EnsureOwnerAsync();
    }

    public class DiscordAdmins : IDiscordAdmins
    {
        public const string ChangeAction = "admins.change";

        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IProvisioningService _provisioning;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        public DiscordAdmins(IDiscordGuildGateway gateway, IGuildSetupService guildSetup, IProvisioningService provisioning, IAuditService audit, Logger logger)
        {
            _gateway = gateway;
            _guildSetup = guildSetup;
            _provisioning = provisioning;
            _audit = audit;
            _logger = logger;
        }

        public async Task<IReadOnlyList<DiscordAdminCandidate>> ListAsync()
        {
            if (!_gateway.IsConnected || _guildSetup.AdminRoleId is not { } role)
            {
                return Array.Empty<DiscordAdminCandidate>();
            }

            var members = await _gateway.GetMembersAsync();

            return members.Where(m => !m.IsBot)
                .Select(m => new DiscordAdminCandidate(Text(m.Id), m.DisplayName, m.Username, m.AvatarUrl, m.RoleIds.Contains(role) || m.Id == _gateway.OwnerId, m.Id == _gateway.OwnerId))
                .OrderByDescending(c => c.IsOwner).ThenByDescending(c => c.IsAdmin).ThenBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        public async Task<DiscordAdminsResult> SetAsync(IReadOnlyCollection<ulong> adminIds, Actor actor)
        {
            if (_provisioning.UsesFile)
            {
                return new DiscordAdminsResult(false, Settings.AppSettingsService.ProvisionedProblem);
            }

            if (!_gateway.IsConnected || _guildSetup.AdminRoleId is not { } role)
            {
                return new DiscordAdminsResult(false, DiscordText.NotConnected);
            }

            var wanted = adminIds.Append(_gateway.OwnerId).Where(id => id != 0).ToHashSet();
            var members = (await _gateway.GetMembersAsync()).Where(m => !m.IsBot).ToList();
            var added = members.Where(m => wanted.Contains(m.Id) && !m.RoleIds.Contains(role)).ToList();
            var removed = members.Where(m => !wanted.Contains(m.Id) && m.RoleIds.Contains(role)).ToList();

            try
            {
                foreach (var member in added)
                {
                    await _gateway.AddRoleToMemberAsync(member.Id, role);
                }

                foreach (var member in removed)
                {
                    await _gateway.RemoveRoleFromMemberAsync(member.Id, role);
                }
            }
            catch (Exception ex)
            {
                // An admin role chosen under Show Advanced that sits above the bot's own can't be given by the bot.
                _logger.Warn(ex, "Couldn't change who holds the admin role");
                return new DiscordAdminsResult(false, "Discord didn't let the bot change who holds the admin role. Move the bot's own role above it in Server Settings, then Roles, and save again.");
            }

            if (added.Any() || removed.Any())
            {
                _audit.Record(actor, ChangeAction, "settings", "admins", Described(added, removed));
            }

            return new DiscordAdminsResult(true, null);
        }

        public async Task EnsureOwnerAsync()
        {
            if (_provisioning.UsesFile || _guildSetup.AdminRoleId is not { } role || _gateway.OwnerId == 0)
            {
                return;
            }

            try
            {
                var owner = await _gateway.GetMemberAsync(_gateway.OwnerId);

                if (owner != null && !owner.RoleIds.Contains(role))
                {
                    await _gateway.AddRoleToMemberAsync(owner.Id, role);
                }
            }
            catch (Exception ex)
            {
                // An admin role above the bot's own (one chosen under Show Advanced) can't be given by the bot.
                _logger.Warn(ex, "Couldn't give the server's owner the admin role");
            }
        }

        // "Admins: added Alex, Sam; removed Kim".
        private static string Described(IReadOnlyCollection<GuildMember> added, IReadOnlyCollection<GuildMember> removed)
        {
            var parts = new List<string>();

            if (added.Any())
            {
                parts.Add($"added {string.Join(", ", added.Select(m => m.DisplayName))}");
            }

            if (removed.Any())
            {
                parts.Add($"removed {string.Join(", ", removed.Select(m => m.DisplayName))}");
            }

            return $"Admins: {string.Join("; ", parts)}";
        }

        private static string Text(ulong id) => id.ToString(CultureInfo.InvariantCulture);
    }
}
