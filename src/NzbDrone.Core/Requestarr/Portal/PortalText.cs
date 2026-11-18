using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Requestarr.Core.Audit;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // The audit log's actions and actors in the words the portal's timelines show.
    public static class PortalText
    {
        public const string Requestarr = "Requestarr";

        private static readonly Dictionary<string, string> Actions = new()
        {
            ["request.create"] = "Requested",
            ["request.subscribe"] = "Followed",
            ["request.approve"] = "Approved",
            ["request.deny"] = "Denied",
            ["request.cancel"] = "Cancelled",
            ["request.send"] = "Sent to Sonarr or Radarr",
            ["request.refused"] = "Request refused",
            ["anime.switch"] = "Sub or dub switched",
            ["access.request"] = "Asked to join",
            ["access.approve"] = "Access approved",
            ["access.deny"] = "Access denied",
            ["access.unblock"] = "Access requests unpaused",
            ["access.block"] = "Access requests paused",
            ["mediaserver.request"] = "Asked to link a media server",
            ["mediaserver.approve"] = "Media server link approved",
            ["mediaserver.deny"] = "Media server link denied",
            ["request.seasons.add"] = "Seasons added",
            ["request.search"] = "Searched again",
            ["request.retry"] = "Tried adding again",
            ["request.progress"] = "Download progress",
            ["request.failed"] = "Download failed",
            ["request.fulfilled"] = "Available",
            ["request.available"] = "Already available",
            ["request.removed"] = "Removed from the library",
            ["removal.start"] = "Removal started",
            ["removal.requester-yes"] = "Requester said remove it",
            ["removal.requester-no"] = "Requester said keep it",
            ["removal.requester-silent"] = "Requester didn't answer: put to a vote",
            ["removal.keep"] = "Keep it pressed",
            ["removal.keep-withdrawn"] = "Keep it taken back",
            ["removal.kept"] = "Kept by the vote",
            ["removal.vote-passed"] = "Vote passed",
            ["removal.force"] = "Forced",
            ["removal.drop"] = "Dropped",
            ["removal.deleted"] = "Deleted",
            ["removal.failed"] = "Deletion failed",
            ["user.record"] = "First seen",
            ["user.admin-record"] = "First seen, as an admin",
            ["user.add"] = "Added",
            ["user.access"] = "Access changed",
            ["user.quota"] = "Limit changed",
            ["user.suspend"] = "Suspended",
            ["user.unsuspend"] = "Unsuspended",
            ["user.remove"] = "Removed",
            ["user.departed"] = "Left the server",
            [MemberNameService.RenameAction] = "Renamed",
            ["mediaserver.link"] = "Media server linked",
            ["settings.change"] = "Setting changed",
            ["instance.add"] = "Instance added",
            ["instance.change"] = "Instance changed"
        };

        // Details recorded before they were written for people, and raw values some still carry: the log keeps what
        // was written, the portal shows it in words. Each rule applies only to the actions that wrote that value (an
        // action starting with Actions), so a title or an admin's reason is never reworded. In order.
        private static readonly (string Actions, Regex Raw, string Words)[] Wording =
        {
            ("request.create", new(@"^(Tv|Movie|Music) (?=.+ on )"), string.Empty),
            ("request.create", new(@"\bseasons seasons\b"), "seasons"),
            ("request.progress", new(@"^None(, |$)"), string.Empty),
            ("user.", new(@"^discord \d+, role \d*, channel \d*, "), string.Empty),
            ("user.remove", new(@"\b(channel|role) \d+ "), "$1 "),
            ("user.", new(@"\bTv\b"), "TV"),
            ("user.", new(@"\bNeedsApproval\b"), "needs approval"),
            ("user.", new(@"\bAutoApprove\b"), "approved automatically"),
            ("user.", new(@"(?<=\w)=(?=\w)"), ": "),
            ("user.", new(@" -> "), " to "),
            ("access.approve", new(@"\bTv\b"), "TV"),
            ("access.approve", new(@"\bNeedsApproval\b"), "needs approval"),
            ("access.approve", new(@"\bAutoApprove\b"), "approved automatically"),
            ("access.approve", new(@"(?<=\w)=(?=\w)"), ": "),
            ("removal.", new(@" at \d{4}-\d\d-\d\d \d\d:\d\d:\d\dZ"), string.Empty),
            ("removal.", new(@"\b1 keep flag\(s\)"), "1 Keep it vote"),
            ("removal.", new(@"\b(\d+) keep flag\(s\)"), "$1 Keep it votes"),
            ("removal.", new(@"\bno keep flags\b"), "no Keep it votes")
        };

        // Where a request was sent, and with what: "Sonarr (test) item 12, profile HD-1080p, root /data/tv, type
        // standard, tag 3"; the tag is Requestarr's own number for it, nothing an admin sees elsewhere.
        private static readonly Regex Sent = new(@"^(?<instance>.+?) item \d+, profile (?<profile>.+?), root (?<rest>.+?)(, tag \d+)?$");

        private static readonly Regex DiscordName = new(@"^discord:(?<id>\d+) \((?<name>.*)\)$");
        private static readonly Regex WebName = new(@"^web(:(?<name>.+))?$");

        // An action Requestarr doesn't name yet shows as it's recorded.
        public static string Action(string action) => Actions.TryGetValue(action ?? string.Empty, out var words) ? words : action;

        // "discord:123 (name)" is that member, "web:name" an admin in the web app; anything else is Requestarr itself.
        public static string Actor(string actor)
        {
            var recorded = actor ?? string.Empty;
            var discord = DiscordName.Match(recorded);

            if (discord.Success)
            {
                return discord.Groups["name"].Value;
            }

            var web = WebName.Match(recorded);

            if (web.Success)
            {
                return web.Groups["name"].Success ? $"{web.Groups["name"].Value} (web app)" : "An admin (web app)";
            }

            return Requestarr;
        }

        // Members' current names by their Discord ID, for entries that recorded only the Discord account.
        public static Dictionary<string, string> DiscordNames(IEnumerable<UserIdentity> identities, IReadOnlyDictionary<int, string> members) =>
            identities.Where(i => i.Provider == UserIdentity.DiscordProvider && members.ContainsKey(i.UserId)).ToDictionary(i => i.ExternalId, i => members[i.UserId]);

        public static PortalEvent Event(AuditEntry entry, IReadOnlyDictionary<int, string> members, IReadOnlyDictionary<string, string> discordNames)
        {
            var who = Who(entry, members, discordNames);

            if (entry.Action == "request.send" && Sent.Match(entry.Detail ?? string.Empty) is { Success: true } sent)
            {
                return new PortalEvent(entry.Timestamp, $"Sent to {sent.Groups["instance"].Value}", who, $"quality profile {sent.Groups["profile"].Value}, root folder {sent.Groups["rest"].Value}");
            }

            return new PortalEvent(entry.Timestamp, Action(entry.Action), who, Detail(entry.Action, entry.Detail));
        }

        // Who: a member by their name now, found by the member or Discord account the entry recorded; else the name it
        // recorded (an admin in the web app, someone who never became a member).
        private static string Who(AuditEntry entry, IReadOnlyDictionary<int, string> members, IReadOnlyDictionary<string, string> discordNames)
        {
            if (entry.ActorUserId is { } id && members.TryGetValue(id, out var name))
            {
                return name;
            }

            var discord = DiscordName.Match(entry.Actor ?? string.Empty);

            return discord.Success && discordNames.TryGetValue(discord.Groups["id"].Value, out var current) ? current : Actor(entry.Actor);
        }

        // Null for nothing left to say (progress with nothing to report).
        public static string Detail(string action, string detail)
        {
            if (string.IsNullOrEmpty(detail))
            {
                return detail;
            }

            // A rename's detail is the member's own names: never reworded.
            if (action == MemberNameService.RenameAction)
            {
                return detail;
            }

            var words = Wording.Where(rule => (action ?? string.Empty).StartsWith(rule.Actions, StringComparison.Ordinal))
                .Aggregate(detail, (text, rule) => rule.Raw.Replace(text, rule.Words));
            return words.Length == 0 ? null : words;
        }
    }
}
