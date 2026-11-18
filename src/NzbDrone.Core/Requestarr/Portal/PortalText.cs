using System.Collections.Generic;
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
            return new PortalEvent(entry.Timestamp, Action(entry.Action), who, entry.Detail);
        }
    }
}
