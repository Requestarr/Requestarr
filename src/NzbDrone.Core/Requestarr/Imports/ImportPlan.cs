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

    public record ImportFollower(ImportPerson Person, DateTime Asked, [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong MessageId);

    // A request the import would create: matched to an item in a Sonarr or Radarr. Seasons: empty for every season.
    // Available: when it finished downloading, as the old bot announced it; HasFiles: whether the library has it now.
    public record PlannedRequest(
        ContentType Type,
        string Title,
        int? Year,
        int? TmdbId,
        int? TvdbId,
        int InstanceId,
        string Instance,
        int ItemId,
        ImportPerson Requester,
        DateTime Requested,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong MessageId,
        [property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong ChannelId,
        IReadOnlyList<int> Seasons,
        IReadOnlyList<int> LibrarySeasons,
        DateTime? Available,
        bool HasFiles,
        IReadOnlyList<ImportFollower> Followers);

    // Someone who asked for a title Requestarr already has a request for: they follow it.
    public record PlannedFollow(int RequestId, string Title, ImportFollower Follower);

    public record SkippedMessage([property: JsonNumberHandling(JsonNumberHandling.WriteAsString)] ulong MessageId, DateTime At, string Who, string What, string Reason);

    // A title the old bot sent that no configured Sonarr or Radarr has (gone since, or never matched): never guessed.
    public record UnmatchedTitle(string Title, ContentType? Type, string Link, DateTime FirstAsked, IReadOnlyList<string> AskedBy);

    // What an import would do, before anything is written. Recorded: the messages a confirmed import marks as done, so a
    // second run skips them (titles nobody's Sonarr or Radarr has stay out, so they're tried again later).
    public record ImportPlan(
        IReadOnlyList<PlannedRequest> Requests,
        IReadOnlyList<PlannedFollow> Follows,
        IReadOnlyList<SkippedMessage> Skipped,
        IReadOnlyList<UnmatchedTitle> Unmatched,
        int MessagesRead,
        int AlreadyImported,
        [property: JsonIgnore] IReadOnlyList<RecordedMessage> Recorded);

    // A message a confirmed import marks as done, and the channel it's in.
    public record RecordedMessage(ulong MessageId, ulong ChannelId);

    // A Sonarr or Radarr title an import can match a request to.
    public record LibraryEntry(int InstanceId, string Instance, ContentType Type, LibraryTitle Title);

    // What an import starts from besides the messages: what's already imported, the libraries, the requests and
    // followers Requestarr has, and who it knows by Discord ID.
    public record ImportContext(
        IReadOnlySet<ulong> Imported,
        IReadOnlyList<LibraryEntry> Library,
        IReadOnlyList<Request> Requests,
        ILookup<int, int> Followers,
        IReadOnlyDictionary<ulong, int> Known);

    public static class ImportPlanner
    {
        public const string AskedAgain = "Asked again by the same person";
        public const string AlreadyAvailable = "The old bot said it was already available";
        public const string NotFound = "The old bot couldn't find it";
        public const string Abandoned = "Left before a title was picked";

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

        private sealed class Builder
        {
            private readonly ImportContext _context;
            private readonly Dictionary<(int Instance, int Item), PlannedRequest> _requests = new();
            private readonly List<PlannedFollow> _follows = new();
            private readonly List<SkippedMessage> _skipped = new();
            private readonly Dictionary<string, UnmatchedTitle> _unmatched = new();
            private readonly List<RecordedMessage> _recorded = new();
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

                switch (message.Kind)
                {
                    case SourceMessageKind.Sent:
                        Sent(message);
                        break;
                    case SourceMessageKind.Finished:
                        Finished(message);
                        break;
                    default:
                        Skip(message, message.Kind switch
                        {
                            SourceMessageKind.AlreadyAvailable => AlreadyAvailable,
                            SourceMessageKind.NotFound => NotFound,
                            _ => Abandoned
                        });
                        break;
                }
            }

            public ImportPlan Build(int read) =>
                new(_requests.Values.OrderBy(r => r.Requested).ToList(), _follows, _skipped, _unmatched.Values.OrderBy(u => u.FirstAsked).ToList(), read, _alreadyImported, _recorded);

            private void Sent(SourceMessage message)
            {
                var person = Person(message);

                if (person == null)
                {
                    return;
                }

                if (Match(message) is not { } entry)
                {
                    Unmatched(message, person);
                    return;
                }

                var follower = new ImportFollower(person, message.Message.Timestamp, message.Message.Id);

                if (Existing(entry) is { } existing)
                {
                    FollowExisting(existing, follower, message);
                    return;
                }

                var key = (entry.InstanceId, entry.Title.Id);

                if (!_requests.TryGetValue(key, out var planned))
                {
                    Record(message);
                    _requests[key] = NewRequest(message, entry, person);
                    return;
                }

                if (planned.Requester.DiscordId == person.DiscordId || planned.Followers.Any(f => f.Person.DiscordId == person.DiscordId))
                {
                    Skip(message, AskedAgain);
                    return;
                }

                Record(message);
                _requests[key] = planned with { Followers = planned.Followers.Append(follower).ToList() };
            }

            private void FollowExisting(Request existing, ImportFollower follower, SourceMessage message)
            {
                var userId = follower.Person.UserId;
                var follows = userId.HasValue && (existing.OwnerUserId == userId || _context.Followers[existing.Id].Contains(userId.Value));

                if (follows || _follows.Any(f => f.RequestId == existing.Id && f.Follower.Person.DiscordId == follower.Person.DiscordId))
                {
                    Skip(message, AskedAgain);
                    return;
                }

                Record(message);
                _follows.Add(new PlannedFollow(existing.Id, existing.Label, follower));
            }

            private static PlannedRequest NewRequest(SourceMessage message, LibraryEntry entry, ImportPerson person)
            {
                var title = entry.Title;
                var hasFiles = entry.Type == ContentType.Movie ? title.HasFile : title.SeasonsWithFiles?.Any() == true;

                return new PlannedRequest(
                    entry.Type,
                    title.Title,
                    title.Year,
                    title.TmdbId,
                    title.TvdbId,
                    entry.InstanceId,
                    entry.Instance,
                    title.Id,
                    person,
                    message.Message.Timestamp,
                    message.Message.Id,
                    message.Message.ChannelId,
                    message.Seasons,
                    title.Seasons ?? Array.Empty<int>(),
                    null,
                    hasFiles,
                    Array.Empty<ImportFollower>());
            }

            // A finished download dates the planned request's availability (its earliest announcement).
            private void Finished(SourceMessage message)
            {
                if (Match(message) is not { } entry)
                {
                    return;
                }

                Record(message);

                if (_requests.TryGetValue((entry.InstanceId, entry.Title.Id), out var planned) && planned.Available is null)
                {
                    _requests[(entry.InstanceId, entry.Title.Id)] = planned with { Available = message.Message.Timestamp };
                }
            }

            private void Skip(SourceMessage message, string reason)
            {
                Record(message);
                _skipped.Add(new SkippedMessage(message.Message.Id, message.Message.Timestamp, Person(message)?.Name, Describe(message), reason));
            }

            private void Record(SourceMessage message) => _recorded.Add(new RecordedMessage(message.Message.Id, message.Message.ChannelId));

            private void Unmatched(SourceMessage message, ImportPerson person)
            {
                var link = Link(message);
                var key = $"{message.Type}:{link}:{message.Title}";

                _unmatched[key] = _unmatched.TryGetValue(key, out var seen)
                    ? seen with { AskedBy = seen.AskedBy.Append(person.Name).Distinct().ToList() }
                    : new UnmatchedTitle(Describe(message), message.Type, link, message.Message.Timestamp, new[] { person.Name });
            }

            private static string Link(SourceMessage message)
            {
                if (message.TvdbId.HasValue)
                {
                    return $"TheTVDB {message.TvdbId}";
                }

                return message.TmdbId.HasValue ? $"The Movie Database {message.TmdbId}" : "no link";
            }

            private static string Describe(SourceMessage message) =>
                message.Year.HasValue && message.Title != null ? $"{message.Title} ({message.Year})" : message.Title;

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

            // By The Movie Database ID in a Radarr, or TheTVDB ID (else The Movie Database's TV ID) in a Sonarr. Never by name.
            private LibraryEntry Match(SourceMessage message) => message.Type switch
            {
                ContentType.Movie => _context.Library.FirstOrDefault(e => e.Type == ContentType.Movie && e.Title.TmdbId == message.TmdbId),
                ContentType.Tv when message.TvdbId.HasValue => _context.Library.FirstOrDefault(e => e.Type == ContentType.Tv && e.Title.TvdbId == message.TvdbId),
                ContentType.Tv when message.TmdbId.HasValue => _context.Library.FirstOrDefault(e => e.Type == ContentType.Tv && e.Title.TmdbId == message.TmdbId),
                _ => null
            };

            // A request Requestarr already has for this title on that instance (not one that was turned down or ended).
            private Request Existing(LibraryEntry entry) =>
                _context.Requests.FirstOrDefault(r => r.BackendId == entry.InstanceId && r.ExternalItemId == entry.Title.Id &&
                                                      r.Status is RequestStatus.Pending or RequestStatus.Approved or RequestStatus.Available);
        }
    }
}
