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
    public interface IMemberNameService
    {
        Task<bool> RefreshAsync(GuildMember member);

        // Everyone's names and pictures read from Discord again (after a start, after an import); asked for while a
        // catch-up is going, it runs once more when that one is done.
        Task CatchUpAsync();
    }

    public class MemberNameService : IMemberNameService, IHandleAsync<DiscordReadyEvent>, IHandleAsync<DiscordMemberSeenEvent>
    {
        public const string RenameAction = "user.rename";

        // Discord.Net waits out Discord's rate limits itself (a channel may be renamed twice in ten minutes); this is for a
        // rename that still failed, tried again later on its own so nothing waits for it. After the last attempt the next
        // start tries again.
        public const int RenameAttempts = 3;
        public static readonly TimeSpan RenameRetryDelay = TimeSpan.FromMinutes(5);

        // A rename's detail, 'from "Old" to "New"; Discord username from @old to @new', read back for the names before.
        // Display names are quoted, so one with " to " in it reads back whole; usernames have no spaces.
        private static readonly Regex FormerName = new(@"from (""(?<name>[^""]+)""|@(?<name>\S+)) to ");

        private readonly IRequestarrUserRepository _users;
        private readonly IUserIdentityRepository _identities;
        private readonly IAccessRequestRepository _accessRequests;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IAuditService _audit;
        private readonly Logger _logger;

        // One person's renames one at a time, so two events for the same rename record it once.
        private readonly ConcurrentDictionary<ulong, SemaphoreSlim> _memberLocks = new();
        private readonly SemaphoreSlim _catchingUp = new(1, 1);

        // Asked for while a catch-up was going: it runs once more when that one is done.
        private int _again;

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
                await TryMatchAsync(user, member, attempt: 1);
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

        // Everyone Requestarr shows by name: members, and everyone who asked to join (waiting, paused, turned down); every
        // member's channel and role are checked too, for a rename that failed earlier.
        public async Task CatchUpAsync()
        {
            Interlocked.Exchange(ref _again, 1);

            if (!await _catchingUp.WaitAsync(0))
            {
                return;
            }

            try
            {
                while (Interlocked.Exchange(ref _again, 0) == 1)
                {
                    await CatchUpOnceAsync();
                }
            }
            finally
            {
                _catchingUp.Release();
            }
        }

        private async Task CatchUpOnceAsync()
        {
            var people = _identities.All().Where(i => i.Provider == UserIdentity.DiscordProvider).Select(i => i.ExternalId)
                .Concat(_accessRequests.All().Select(r => r.DiscordUserId))
                .Distinct()
                .Select(id => ulong.TryParse(id, out var parsed) ? parsed : 0)
                .Where(id => id != 0)
                .ToList();
            var renamed = 0;
            var discordRenames = 0;

            // One at a time: each is a call to Discord. Each person is read from Discord once their turn comes, so a newer
            // rename handled meanwhile is never undone by an older reading.
            foreach (var id in people)
            {
                var (wasRenamed, matched) = await OneAtATimeAsync(id, () => ReadAndMatchAsync(id, attempt: 1));
                renamed += wasRenamed ? 1 : 0;
                discordRenames += matched;
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
        // Someone known only from an import is in the server now (they joined, did something there, or were found at
        // start): Imported, not onboarded.
        private (RequestarrUser User, bool Renamed) RefreshNames(GuildMember member)
        {
            RefreshAccessRequests(member);

            var identity = _identities.Find(UserIdentity.DiscordProvider, member.Id.ToString());
            var user = identity == null ? null : _users.Find(identity.UserId);

            if (user?.Status is UserStatus.ImportedLeft or UserStatus.ImportedElsewhere && _users.MarkInServer(user.Id, DateTime.UtcNow))
            {
                user.Status = UserStatus.Imported;
            }

            return user == null ? (null, false) : (user, Rename(user, identity, member));
        }

        // Someone not in this server. Known only from an import: their @handle, name and picture as Discord has them, so
        // they're shown like everyone else; nothing they did, so no member event. Anyone else (a member who left) keeps
        // the names they had here, and only their picture follows.
        private async Task RefreshElsewhereAsync(ulong discordUserId)
        {
            var identity = _identities.Find(UserIdentity.DiscordProvider, discordUserId.ToString());

            if (identity == null || await UserAsync(discordUserId) is not { } found)
            {
                return;
            }

            identity.AvatarUrl = found.AvatarUrl ?? identity.AvatarUrl;
            KeepProfile(identity, found);

            if (_users.Find(identity.UserId) is { IsImported: true } user)
            {
                identity.Username = found.Username;
                identity.DisplayName = found.DisplayName;

                if (user.DisplayName != found.DisplayName)
                {
                    user.DisplayName = found.DisplayName;
                    _users.SetFields(user, u => u.DisplayName);
                }
            }

            _identities.SetFields(identity, i => i.Username, i => i.DisplayName, i => i.AvatarUrl, i => i.BannerUrl, i => i.AccentColor);
        }

        // A member's profile banner and accent colour: only Discord's user lookup has them, so it's one more call for each
        // person, once a catch-up. Someone Discord can't read now keeps what they had.
        private async Task RefreshProfileAsync(ulong discordUserId)
        {
            var identity = _identities.Find(UserIdentity.DiscordProvider, discordUserId.ToString());

            if (identity != null && await UserAsync(discordUserId) is { } found && KeepProfile(identity, found))
            {
                _identities.SetFields(identity, i => i.BannerUrl, i => i.AccentColor);
            }
        }

        // True when Discord's banner or colour changed (put up, changed or taken down).
        private static bool KeepProfile(UserIdentity identity, GuildMember found)
        {
            if (identity.BannerUrl == found.BannerUrl && identity.AccentColor == found.AccentColor)
            {
                return false;
            }

            identity.BannerUrl = found.BannerUrl;
            identity.AccentColor = found.AccentColor;

            return true;
        }

        // Stores the names Discord has now; a change is a member event: 'from "X" to "Y"'. A new picture is kept quietly.
        private bool Rename(RequestarrUser user, UserIdentity identity, GuildMember member)
        {
            if (member.AvatarUrl != null && identity.AvatarUrl != member.AvatarUrl)
            {
                identity.AvatarUrl = member.AvatarUrl;
                _identities.SetFields(identity, i => i.AvatarUrl);
            }

            var changes = new List<string>();

            if (user.DisplayName != member.DisplayName)
            {
                changes.Add($"from \"{user.DisplayName}\" to \"{member.DisplayName}\"");
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

            // Only the name: anything else on the record may have changed meanwhile (an onboarding, say).
            user.DisplayName = member.DisplayName;
            _users.SetFields(user, u => u.DisplayName);

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

        // The channel and role renamed to match; a failure is tried again later, on its own. The number renamed now.
        private async Task<int> TryMatchAsync(RequestarrUser user, GuildMember member, int attempt)
        {
            try
            {
                return await MatchChannelAndRoleAsync(user, member);
            }
            catch (Exception ex) when (attempt < RenameAttempts)
            {
                _logger.Warn(ex, "Renaming member {0}'s channel or role failed, trying again in {1}", user.Id, RetryDelay);
                _ = RetryLaterAsync(member.Id, attempt + 1);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Renaming member {0}'s channel or role failed {1} times; the next start tries again", user.Id, RenameAttempts);
            }

            return 0;
        }

        // A retry reads the member from Discord again: whatever they're called by then is what their channel gets.
        private async Task RetryLaterAsync(ulong discordUserId, int attempt)
        {
            await Task.Delay(RetryDelay);

            try
            {
                await OneAtATimeAsync(discordUserId, () => ReadAndMatchAsync(discordUserId, attempt));
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Trying member {0}'s rename again failed; the next start tries again", discordUserId);
            }
        }

        // The person read from Discord now: whether their stored names changed, and how many of their channel and role
        // were renamed to match (none for someone not added, or who can't be read).
        private async Task<(bool Renamed, int Matched)> ReadAndMatchAsync(ulong discordUserId, int attempt)
        {
            var (member, read) = await MemberAsync(discordUserId);

            // Not read (Discord didn't answer): nothing changes. Not in this server: their details from Discord itself.
            if (member == null)
            {
                if (read)
                {
                    await RefreshElsewhereAsync(discordUserId);
                }

                return (false, 0);
            }

            var (user, renamed) = RefreshNames(member);
            await RefreshProfileAsync(discordUserId);

            return (renamed, user == null ? 0 : await TryMatchAsync(user, member, attempt));
        }

        // The channel and role are named after the Discord username, as when the member was added (the channel archived
        // once they're removed), and the channel's topic after their display name. Names are compared exactly, so two
        // members whose names read the same in a channel name never take turns renaming. The number renamed.
        private async Task<int> MatchChannelAndRoleAsync(RequestarrUser user, GuildMember member)
        {
            var renamed = 0;
            var channel = ulong.TryParse(user.PrivateChannelId, out var channelId) ? (await _gateway.GetTextChannelsAsync()).FirstOrDefault(c => c.Id == channelId) : null;

            if (channel != null)
            {
                var name = ChannelLayout.UserChannelName(member.Username);
                name = user.Status == UserStatus.Removed ? ChannelLayout.ArchivedName(name) : name;

                if (channel.Name != name)
                {
                    await _gateway.RenameChannelAsync(channelId, name);
                    renamed++;
                }

                await _gateway.SetChannelTopicAsync(channelId, ChannelLayout.UserChannelTopic(member.DisplayName));
            }

            if (ulong.TryParse(user.DiscordRoleId, out var roleId) && await _gateway.RenameRoleAsync(roleId, ChannelLayout.UserRoleName(member.Username)))
            {
                renamed++;
            }

            return renamed;
        }

        // Someone Discord can't read now keeps the names and picture they had.
        private async Task<GuildMember> UserAsync(ulong id)
        {
            try
            {
                return await _gateway.GetUserAsync(id);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Couldn't read Discord user {0}", id);

                return null;
            }
        }

        // Someone who left the server (or can't be read now) keeps the names they had.
        // Read: whether Discord answered (a member, or that they aren't one).
        private async Task<(GuildMember Member, bool Read)> MemberAsync(ulong id)
        {
            try
            {
                return (await _gateway.GetMemberAsync(id), true);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Couldn't read Discord member {0} to catch up their names", id);

                return (null, false);
            }
        }
    }
}
