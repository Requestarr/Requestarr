using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Access;
using Requestarr.Core.Announcements;
using Requestarr.Core.Audit;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;

namespace Requestarr.Core.Users
{
    // Everything that identifies a person is their Discord ID; their names are only for showing, so they follow Discord:
    // on a rename, on any press, and at every start for a rename missed while the bot was offline.
    public class MemberNameService : IHandleAsync<DiscordReadyEvent>, IHandleAsync<DiscordMemberSeenEvent>
    {
        public const string RenameAction = "user.rename";

        // Discord.Net waits out Discord's rate limits itself (a channel may be renamed twice in ten minutes); this is for a
        // rename that still failed. After the last attempt the next start tries again.
        public const int RenameAttempts = 3;
        public static readonly TimeSpan RenameRetryDelay = TimeSpan.FromMinutes(5);

        // A rename's detail, "from Old to New; Discord username from @old to @new", read back for the names before.
        private static readonly Regex FormerName = new(@"from @?(?<name>.+?) to ");

        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly IAccessRequestRepository _accessRequests;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        // One person's renames one at a time, so two events for the same rename record it once.
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _memberLocks = new();

        public MemberNameService(IRequestarrUserRepository users,
                                 IUserIdentityRepository identities,
                                 IAccessRequestRepository accessRequests,
                                 IDiscordGuildGateway gateway,
                                 IAuditService audit,
                                 Logger logger)
        {
            _users = users;
            _identities = identities;
            _accessRequests = accessRequests;
            _gateway = gateway;
            _audit = audit;
            _logger = logger;
        }

        public TimeSpan RetryDelay { get; set; } = RenameRetryDelay;

        // The names a rename's detail says the member had, so they can still be found by them.
        public static IEnumerable<string> FormerNames(string renameDetail) =>
            FormerName.Matches(renameDetail ?? string.Empty).Select(m => m.Groups["name"].Value);

        // True when a member's names changed; their channel and role are then renamed to match.
        public Task<bool> RefreshAsync(GuildMember member) => OneAtATimeAsync(member.Id, async () =>
        {
            var (user, renamed) = RefreshNames(member);

            if (renamed)
            {
                await MatchWithRetriesAsync(user, member);
            }

            return renamed;
        });

        public void HandleAsync(DiscordReadyEvent message)
        {
            CatchUpAsync().GetAwaiter().GetResult();
        }

        public void HandleAsync(DiscordMemberSeenEvent message)
        {
            RefreshAsync(message.Member).GetAwaiter().GetResult();
        }

        // Everyone Requestarr shows by name, members and people waiting to join; every member's channel and role are
        // checked too, for a rename that failed earlier.
        public async Task CatchUpAsync()
        {
            var people = _identities.All().Where(i => i.Provider == UserIdentity.DiscordProvider).Select(i => i.ExternalId)
                .Concat(_accessRequests.Pending().Select(r => r.DiscordUserId))
                .Distinct()
                .Select(id => ulong.TryParse(id, out var parsed) ? parsed : 0)
                .Where(id => id != 0)
                .ToList();
            var (renamed, discordRenames) = (0, 0);

            // One at a time: each is a call to Discord.
            foreach (var id in people)
            {
                var member = await MemberAsync(id);

                if (member == null)
                {
                    continue;
                }

                discordRenames += await OneAtATimeAsync(member.Id, async () =>
                {
                    var (user, wasRenamed) = RefreshNames(member);
                    renamed += wasRenamed ? 1 : 0;

                    return user == null ? 0 : await MatchWithRetriesAsync(user, member);
                });
            }

            _logger.Info("Names caught up: {0} of {1} people renamed since, {2} channels or roles renamed to match", renamed, people.Count, discordRenames);
        }

        private async Task<T> OneAtATimeAsync<T>(ulong discordUserId, Func<Task<T>> work)
        {
            var gate = _memberLocks.GetOrAdd(discordUserId, _ => new SemaphoreSlim(1, 1));
            await gate.WaitAsync();

            try
            {
                return await work();
            }
            finally
            {
                gate.Release();
            }
        }

        // The person's stored names as Discord has them now, and their member record (null for someone not added).
        private (RequestarrUser User, bool Renamed) RefreshNames(GuildMember member)
        {
            RefreshAccessRequests(member);

            var identity = _identities.Find(UserIdentity.DiscordProvider, member.Id.ToString());
            var user = identity == null ? null : _users.Find(identity.UserId);

            return user == null ? (null, false) : (user, Rename(user, identity, member));
        }

        // Stores the names Discord has now; a change is a member event: "from X to Y".
        private bool Rename(RequestarrUser user, UserIdentity identity, GuildMember member)
        {
            var changes = new List<string>();

            if (user.DisplayName != member.DisplayName)
            {
                changes.Add($"from {user.DisplayName} to {member.DisplayName}");
            }

            if (identity.Username != member.Username)
            {
                changes.Add($"Discord username from @{identity.Username} to @{member.Username}");
            }

            if (changes.Count == 0)
            {
                return false;
            }

            identity.Username = member.Username;
            identity.DisplayName = member.DisplayName;
            identity.AvatarUrl = member.AvatarUrl ?? identity.AvatarUrl;
            _identities.Update(identity);

            user.DisplayName = member.DisplayName;
            _users.Update(user);

            _audit.Record(Actor.Discord(member.Id, member.Username, user.Id), RenameAction, "user", user.Id.ToString(), string.Join("; ", changes));
            _logger.Info("Member {0} renamed: {1}", user.Id, string.Join("; ", changes));

            return true;
        }

        // A person waiting to join (or paused) is shown by the names they asked with: those follow Discord too.
        private void RefreshAccessRequests(GuildMember member)
        {
            foreach (var request in _accessRequests.ForMember(member.Id.ToString()).Where(r => r.Username != member.Username || r.DisplayName != member.DisplayName))
            {
                request.Username = member.Username;
                request.DisplayName = member.DisplayName;
                _accessRequests.Update(request);
            }
        }

        private async Task<int> MatchWithRetriesAsync(RequestarrUser user, GuildMember member)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return await MatchChannelAndRoleAsync(user, member);
                }
                catch (Exception ex) when (attempt < RenameAttempts)
                {
                    _logger.Warn(ex, "Renaming member {0}'s channel or role failed, trying again in {1}", user.Id, RetryDelay);
                    await Task.Delay(RetryDelay);
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Renaming member {0}'s channel or role failed {1} times; the next start tries again", user.Id, RenameAttempts);

                    return 0;
                }
            }
        }

        // The channel and role are named after the Discord username, as when the member was added (the channel archived
        // once they're removed), and the channel's topic after their display name. The number renamed.
        private async Task<int> MatchChannelAndRoleAsync(RequestarrUser user, GuildMember member)
        {
            var renamed = 0;

            if (ulong.TryParse(user.PrivateChannelId, out var channelId) && await _gateway.ChannelExistsAsync(channelId))
            {
                var name = ChannelLayout.UserChannelName(member.Username);
                name = user.Status == UserStatus.Removed ? ChannelLayout.ArchivedName(name) : name;

                if (await _gateway.FindTextChannelAsync(name) != channelId)
                {
                    await _gateway.RenameChannelAsync(channelId, name);
                    renamed++;
                }

                await _gateway.SetChannelTopicAsync(channelId, ChannelLayout.UserChannelTopic(member.DisplayName));
            }

            var roleName = ChannelLayout.UserRoleName(member.Username);

            if (ulong.TryParse(user.DiscordRoleId, out var roleId) && await _gateway.RoleExistsAsync(roleId) && await _gateway.FindRoleAsync(roleName) != roleId)
            {
                await _gateway.RenameRoleAsync(roleId, roleName);
                renamed++;
            }

            return renamed;
        }

        // Someone who left the server (or can't be read now) keeps the names they had.
        private async Task<GuildMember> MemberAsync(ulong id)
        {
            try
            {
                return await _gateway.GetMemberAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't read Discord member {0} to catch up their names", id);

                return null;
            }
        }
    }
}
