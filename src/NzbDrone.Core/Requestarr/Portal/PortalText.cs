using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Requestarr.Core.Audit;

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
            ["mediaserver.link"] = "Media server linked",
            ["settings.change"] = "Setting changed",
            ["instance.add"] = "Instance added",
            ["instance.change"] = "Instance changed"
        };

        // Details recorded before they were written for people, and raw values some still carry: the log keeps what
        // was written, the portal shows it in words. In order; each applies wherever it matches.
        private static readonly (Regex Raw, string Words)[] Wording =
        {
            (new(@"^(Tv|Movie|Music) (?=.+ on )"), string.Empty),
            (new(@"\bseasons seasons\b"), "seasons"),
            (new(@"^None(, |$)"), string.Empty),
            (new(@"\bTv\b"), "TV"),
            (new(@"\bNeedsApproval\b"), "needs approval"),
            (new(@"\bAutoApprove\b"), "approved automatically"),
            (new(@"(?<=\w)=(?=\w)"), ": "),
            (new(@" -> "), " to "),
            (new(@"^discord \d+, role \d*, channel \d*, "), string.Empty),
            (new(@"\b(channel|role) \d+ "), "$1 "),
            (new(@" at \d{4}-\d\d-\d\d \d\d:\d\d:\d\dZ"), string.Empty),
            (new(@"\b1 keep flag\(s\)"), "1 Keep it vote"),
            (new(@"\b(\d+) keep flag\(s\)"), "$1 Keep it votes"),
            (new(@"\bno keep flags\b"), "no Keep it votes")
        };

        // Where a request was sent, and with what: "Sonarr (test) item 12, profile HD-1080p, root /data/tv".
        private static readonly Regex Sent = new(@"^(?<instance>.+?) item \d+, profile (?<profile>.+?), root (?<rest>.+)$");

        private static readonly Regex DiscordName = new(@"^discord:\d+ \((?<name>.*)\)$");
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

        // Who: a member as Requestarr knows them when the entry says which member it was, else the name it recorded.
        public static PortalEvent Event(AuditEntry entry, IReadOnlyDictionary<int, string> members)
        {
            var who = entry.ActorUserId is { } id && members.TryGetValue(id, out var name) ? name : Actor(entry.Actor);

            if (entry.Action == "request.send" && Sent.Match(entry.Detail ?? string.Empty) is { Success: true } sent)
            {
                return new PortalEvent(entry.Timestamp, $"Sent to {sent.Groups["instance"].Value}", who, $"quality profile {sent.Groups["profile"].Value}, root folder {sent.Groups["rest"].Value}");
            }

            return new PortalEvent(entry.Timestamp, Action(entry.Action), who, Detail(entry.Detail));
        }

        // Null for nothing left to say (progress with nothing to report).
        public static string Detail(string detail)
        {
            if (string.IsNullOrEmpty(detail))
            {
                return detail;
            }

            var words = Wording.Aggregate(detail, (text, rule) => rule.Raw.Replace(text, rule.Words));
            return words.Length == 0 ? null : words;
        }
    }
}
