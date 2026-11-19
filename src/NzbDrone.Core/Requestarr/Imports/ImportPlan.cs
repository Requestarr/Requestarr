using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using Requestarr.Core.Backends;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Imports
{
    // Someone who asked in the old channel: by Discord ID; UserId when Requestarr already knows them.
    public record ImportPerson([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong DiscordId, string Name, int? UserId);

    public record ImportFollower(ImportPerson Person, DateTime Asked);

    // How a request ended in the old bot: sent to Sonarr or Radarr, or answered that it was already available.
    public enum ImportOutcome
    {
        Sent,
        AlreadyAvailable
    }

    // A request the import would create. In the library when an item in a Sonarr or Radarr matches it by ID (InstanceId,
    // Instance and ItemId set); otherwise it's kept as history, Not in library. Seasons: empty for every season.
    // Available: when it finished downloading, as the old bot announced it; HasFiles: whether the library has it now.
    public record PlannedRequest(
        ContentType Type,
        string Title,
        int? Year,
        int? TmdbId,
        int? TvdbId,
        int? InstanceId,
        string Instance,
        int? ItemId,
        ImportPerson Requester,
        DateTime Requested,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong ChannelId,
        IReadOnlyList<int> Seasons,
        IReadOnlyList<int> LibrarySeasons,
        DateTime? Available,
        bool HasFiles,
        ImportOutcome Outcome,
        IReadOnlyList<ImportFollower> Followers)
    {
        public bool InLibrary => ItemId.HasValue;
    }

    // A request Requestarr already has for a title people asked the old bot for: they follow it.
    public record PlannedFollow(int RequestId, ContentType Type, string Title, int? Year, string Requester, DateTime Requested, string Instance, IReadOnlyList<ImportFollower> Followers);

    // An earlier import's request that wasn't in the library and now is: it's linked to the item.
    public record PlannedLink(int RequestId, string Title, int InstanceId, string Instance, int ItemId, IReadOnlyList<int> LibrarySeasons, bool HasFiles);

    // What an import would do, before anything is written: one list of every request found (Requests, and Follows for
    // titles Requestarr already has), library links for earlier imports, and counts of what isn't a request. Recorded:
    // the messages a confirmed import marks as done, so a second run skips them.
    public record ImportPlan(
        IReadOnlyList<PlannedRequest> Requests,
        IReadOnlyList<PlannedFollow> Follows,
        IReadOnlyList<PlannedLink> Links,
        int NotRequests,
        int NoRequester,
        int MessagesRead,
        int AlreadyImported,
        [property: JsonIgnore] IReadOnlyList<RecordedMessage> Recorded);

    // A message a confirmed import marks as done, and the channel it's in.
    public record RecordedMessage(ulong MessageId, ulong ChannelId);

    // A Sonarr or Radarr title an import can match a request to.
    public record LibraryEntry(int InstanceId, string Instance, ContentType Type, LibraryTitle Title);

    // What an import starts from besides the messages: what's already imported, the libraries, the requests and
    // followers Requestarr has, who it knows by Discord ID, and members' names by their ID.
    public record ImportContext(
        IReadOnlySet<ulong> Imported,
        IReadOnlyList<LibraryEntry> Library,
        IReadOnlyList<Request> Requests,
        ILookup<int, int> Followers,
        IReadOnlyDictionary<ulong, int> Known,
        IReadOnlyDictionary<int, string> Names);

    public static class ImportPlanner
    {
        // Oldest first: the first to ask is the requester, everyone after a follower, as Requestarr does today.
        public static ImportPlan Plan(IReadOnlyList<SourceMessage> messages, ImportContext context)
        {
            var builder = new Builder(context);

            foreach (var message in messages.OrderBy(m => m.Message.Timestamp).ThenBy(m => m.Message.Id))
            {
                builder.Add(message);
            }

            return builder.Build(messages.Count);
        }

        // The title a message names: its type and The Movie Database or TheTVDB ID, or its name when it has no link.
        private readonly record struct TitleKey(ContentType? Type, int? TmdbId, int? TvdbId, string Name);

        private sealed class Builder
        {
            private readonly ImportContext _context;
            private readonly Dictionary<TitleKey, PlannedRequest> _requests = new();
            private readonly Dictionary<int, PlannedFollow> _follows = new();
            private readonly List<RecordedMessage> _recorded = new();
            private int _notRequests;
            private int _noRequester;
            private int _alreadyImported;

            public Builder(ImportContext context)
            {
                _context = context;
            }

            public void Add(SourceMessage message)
            {
                if (message.Kind == SourceMessageKind.Other)
                {
                    return;
                }

                if (_context.Imported.Contains(message.Message.Id))
                {
                    _alreadyImported++;
                    return;
                }

                Record(message);

                switch (message.Kind)
                {
                    case SourceMessageKind.Sent:
                        Asked(message, ImportOutcome.Sent);
                        break;
                    case SourceMessageKind.AlreadyAvailable:
                        Asked(message, ImportOutcome.AlreadyAvailable);
                        break;
                    case SourceMessageKind.Finished:
                        Finished(message);
                        break;
                    default:
                        _notRequests++;
                        break;
                }
            }

            public ImportPlan Build(int read) =>
                new(_requests.Values.OrderBy(r => r.Requested).ToList(), _follows.Values.OrderBy(f => f.Requested).ToList(), Links(), _notRequests, _noRequester, read, _alreadyImported, _recorded);

            private void Asked(SourceMessage message, ImportOutcome outcome)
            {
                var person = Person(message);

                if (person == null)
                {
                    _noRequester++;
                    return;
                }

                // A reply with no card names no title: nothing to record as asked for.
                if (message.Type == null)
                {
                    _notRequests++;
                    return;
                }

                var entry = Match(message);
                var follower = new ImportFollower(person, message.Message.Timestamp);

                if (Existing(message, entry) is { } existing)
                {
                    FollowExisting(existing, follower);
                    return;
                }

                var key = Key(message, entry);

                if (!_requests.TryGetValue(key, out var planned))
                {
                    _requests[key] = NewRequest(message, entry, person, outcome);
                    return;
                }

                var asked = planned.Requester.DiscordId == person.DiscordId || planned.Followers.Any(f => f.Person.DiscordId == person.DiscordId);

                _requests[key] = planned with { Seasons = MergedSeasons(planned.Seasons, message.Seasons), Followers = asked ? planned.Followers : planned.Followers.Append(follower).ToList() };
            }

            // The seasons of a show everyone asked for: none listed means every season, so that wins.
            private static IReadOnlyList<int> MergedSeasons(IReadOnlyList<int> planned, IReadOnlyList<int> asked)
            {
                if (planned.Count == 0 || asked.Count == 0)
                {
                    return Array.Empty<int>();
                }

                return planned.Union(asked).OrderBy(s => s).ToList();
            }

            private void FollowExisting(Request existing, ImportFollower follower)
            {
                var userId = follower.Person.UserId;
                var follows = userId.HasValue && (existing.OwnerUserId == userId || _context.Followers[existing.Id].Contains(userId.Value));
                var planned = _follows.GetValueOrDefault(existing.Id) ?? Following(existing);

                if (follows || planned.Followers.Any(f => f.Person.DiscordId == follower.Person.DiscordId))
                {
                    return;
                }

                _follows[existing.Id] = planned with { Followers = planned.Followers.Append(follower).ToList() };
            }

            private PlannedFollow Following(Request existing) =>
                new(existing.Id, existing.ContentType, existing.Title, existing.Year, _context.Names.GetValueOrDefault(existing.OwnerUserId), existing.Requested, Instance(existing.BackendId), Array.Empty<ImportFollower>());

            private string Instance(int? instanceId) => _context.Library.FirstOrDefault(e => e.InstanceId == instanceId)?.Instance;

            private static PlannedRequest NewRequest(SourceMessage message, LibraryEntry entry, ImportPerson person, ImportOutcome outcome)
            {
                var title = entry?.Title;

                return new PlannedRequest(
                    message.Type.Value,
                    title?.Title ?? message.Title,
                    title?.Year ?? message.Year,
                    title?.TmdbId ?? message.TmdbId,
                    title?.TvdbId ?? message.TvdbId,
                    entry?.InstanceId,
                    entry?.Instance,
                    title?.Id,
                    person,
                    message.Message.Timestamp,
                    message.Message.ChannelId,
                    message.Seasons,
                    title?.Seasons ?? Array.Empty<int>(),
                    null,
                    entry != null && HasFiles(entry),
                    outcome,
                    Array.Empty<ImportFollower>());
            }

            // A finished download dates the planned request's availability (its earliest announcement).
            private void Finished(SourceMessage message)
            {
                var key = Key(message, Match(message));

                if (_requests.TryGetValue(key, out var planned) && planned.Available is null)
                {
                    _requests[key] = planned with { Available = message.Message.Timestamp };
                }
            }

            private void Record(SourceMessage message) => _recorded.Add(new RecordedMessage(message.Message.Id, message.Message.ChannelId));

            // Earlier imports' requests that weren't in the library, now matched by ID.
            private List<PlannedLink> Links() =>
                _context.Requests.Where(r => r.Status == RequestStatus.NotInLibrary)
                    .Select(r => (Request: r, Entry: Find(r.ContentType, r.TmdbId, r.TvdbId)))
                    .Where(m => m.Entry != null)
                    .Select(m => new PlannedLink(m.Request.Id, m.Request.Label, m.Entry.InstanceId, m.Entry.Instance, m.Entry.Title.Id, m.Entry.Title.Seasons ?? Array.Empty<int>(), HasFiles(m.Entry)))
                    .ToList();

            private static bool HasFiles(LibraryEntry entry) =>
                entry.Type == ContentType.Movie ? entry.Title.HasFile : entry.Title.SeasonsWithFiles?.Any() == true;

            private ImportPerson Person(SourceMessage message)
            {
                if (message.People.Count == 0)
                {
                    return null;
                }

                var id = message.People[0];
                var name = message.Message.InvokedById == id ? message.Message.InvokedByName : null;

                return new ImportPerson(id, name ?? id.ToString(), _context.Known.TryGetValue(id, out var userId) ? userId : null);
            }

            private LibraryEntry Match(SourceMessage message) => message.Type is { } type ? Find(type, message.TmdbId, message.TvdbId) : null;

            // By The Movie Database ID in a Radarr, or TheTVDB ID (else The Movie Database's TV ID) in a Sonarr. Never by name.
            private LibraryEntry Find(ContentType type, int? tmdbId, int? tvdbId) => type switch
            {
                ContentType.Movie when tmdbId.HasValue => _context.Library.FirstOrDefault(e => e.Type == ContentType.Movie && e.Title.TmdbId == tmdbId),
                ContentType.Tv when tvdbId.HasValue => _context.Library.FirstOrDefault(e => e.Type == ContentType.Tv && e.Title.TvdbId == tvdbId),
                ContentType.Tv when tmdbId.HasValue => _context.Library.FirstOrDefault(e => e.Type == ContentType.Tv && e.Title.TmdbId == tmdbId),
                _ => null
            };

            // The library's IDs when it has the title (they're complete), the message's otherwise; its name only when
            // it has no link at all. A show is one show by its TheTVDB ID whichever link named it.
            private static TitleKey Key(SourceMessage message, LibraryEntry entry)
            {
                var tmdb = entry?.Title.TmdbId ?? message.TmdbId;
                var tvdb = entry?.Title.TvdbId ?? message.TvdbId;

                if (message.Type == ContentType.Tv && tvdb.HasValue)
                {
                    return new TitleKey(ContentType.Tv, null, tvdb, null);
                }

                return new TitleKey(message.Type, tmdb, tvdb, tmdb.HasValue || tvdb.HasValue ? null : message.Title);
            }

            // A request Requestarr already has for this title, on any instance: one that's waiting, sent or available, else
            // an earlier import's Not in library one (not one that was turned down or ended). By The Movie Database ID for
            // a movie, TheTVDB ID for a show, as requests do.
            private Request Existing(SourceMessage message, LibraryEntry entry)
            {
                var type = message.Type.Value;
                var id = type == ContentType.Movie ? entry?.Title.TmdbId ?? message.TmdbId : entry?.Title.TvdbId ?? message.TvdbId;

                if (!id.HasValue)
                {
                    return null;
                }

                var forTitle = _context.Requests.Where(r => r.ContentType == type && r.ExternalId == id).ToList();

                return forTitle.FirstOrDefault(r => r.IsActive) ?? forTitle.FirstOrDefault(r => r.Status == RequestStatus.NotInLibrary);
            }
        }
    }
}
