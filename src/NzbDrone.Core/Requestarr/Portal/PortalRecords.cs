using System;
using System.Collections.Generic;
using System.Linq;
using Requestarr.Core.Access;
using Requestarr.Core.Announcements;
using Requestarr.Core.Backends;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // Someone asking to join from the welcome channel, waiting on an admin: the types they'd like and their note.
    public record PortalAccessRequest(int Id, string DiscordId, string Name, string Username, IReadOnlyList<ContentType> ContentTypes, string Note, DateTime Requested);

    // Someone whose access requests are paused after too many clicks; Since: when.
    public record PortalBlocked(string DiscordId, string Name, DateTime Since);

    public record PortalAccessRequests(IReadOnlyList<PortalAccessRequest> Waiting, IReadOnlyList<PortalBlocked> Paused);

    // An instance down now: since when, and whether members were told (and by whom).
    public record PortalOutageNow(int InstanceId, string Instance, DateTime Since, bool Announced, string AnnouncedBy);

    // One outage long enough to alert the admins, from Requestarr's lasting record.
    public record PortalOutage(string Instance, DateTime Started, DateTime? Ended, DateTime? Announced, string AnnouncedBy);

    public record PortalOutages(IReadOnlyList<PortalOutageNow> Now, IReadOnlyList<PortalOutage> History);

    // One version's changes, as its update card's Read changes has them; Version is its label (0.7.0004-dev.4f2a9c1).
    public record PortalVersion(string Version, DateTime? Date, IReadOnlyList<string> Users, IReadOnlyList<string> Admins);

    // A Sonarr or Radarr a member's requests can go to.
    public record PortalInstance(int Id, string Name, ContentType ContentType, bool Enabled);

    public interface IPortalRecords
    {
        PortalAccessRequests AccessRequests();
        PortalOutages Outages();
        IReadOnlyList<PortalInstance> Instances();
        IReadOnlyList<PortalVersion> Changes();
    }

    // The portal's views of what has no page of its own in 5a: people asking to join, outages, the instances a member
    // can be given, and each version's changes.
    public class PortalRecords : IPortalRecords
    {
        private readonly IAccessRequestRepository _accessRequests;
        private readonly IWelcomeGuardRepository _guards;
        private readonly IBackendRepository _backends;
        private readonly IOutageRepository _outages;
        private readonly IAnnouncementService _announcements;

        public PortalRecords(IAccessRequestRepository accessRequests,
                             IWelcomeGuardRepository guards,
                             IBackendRepository backends,
                             IOutageRepository outages,
                             IAnnouncementService announcements)
        {
            _accessRequests = accessRequests;
            _guards = guards;
            _backends = backends;
            _outages = outages;
            _announcements = announcements;
        }

        public PortalAccessRequests AccessRequests()
        {
            var waiting = _accessRequests.Pending()
                .OrderBy(r => r.Requested)
                .Select(r => new PortalAccessRequest(r.Id, r.DiscordUserId, r.DisplayName ?? r.Username, r.Username, r.ContentTypes ?? new List<ContentType>(), r.Note, r.Requested))
                .ToList();

            var paused = _guards.AllBlocked().Select(g => new PortalBlocked(g.DiscordUserId, NameOf(g.DiscordUserId), g.Blocked ?? DateTime.UtcNow)).ToList();

            return new PortalAccessRequests(waiting, paused);
        }

        public PortalOutages Outages()
        {
            var now = _backends.All()
                .Where(b => b.Enable && b.DownSince.HasValue)
                .Select(b => (Instance: b, Record: _outages.Ongoing(b.Id)))
                .Select(d => new PortalOutageNow(d.Instance.Id, d.Instance.Name, d.Instance.DownSince.Value, d.Instance.DownAnnounceMessageId != null, d.Record?.AnnouncedBy))
                .ToList();

            var history = _outages.History().Select(o => new PortalOutage(o.InstanceName, o.Started, o.Ended, o.Announced, o.AnnouncedBy)).ToList();

            return new PortalOutages(now, history);
        }

        public IReadOnlyList<PortalInstance> Instances() =>
            _backends.All()
                .Where(b => b.Implementation is BackendImplementations.Sonarr or BackendImplementations.Radarr)
                .OrderBy(b => b.Name)
                .Select(b => new PortalInstance(b.Id, b.Name, Discord.DiscordText.TypeOf(b), b.Enable))
                .ToList();

        public IReadOnlyList<PortalVersion> Changes() =>
            _announcements.VersionNotes().Select(n => new PortalVersion(n.Shown, n.Version.Date, n.Version.Users, n.Version.Admins)).ToList();

        // A paused person isn't a member: their name is the one they last asked to join with.
        private string NameOf(string discordId)
        {
            var asked = _accessRequests.ForMember(discordId).LastOrDefault();
            return asked?.DisplayName ?? asked?.Username ?? discordId;
        }
    }
}
