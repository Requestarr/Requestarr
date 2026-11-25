using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Anime;
using Requestarr.Core.Audit;
using Requestarr.Core.Availability;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Metadata;
using Requestarr.Core.SeriesFixes;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // Who is requesting: a Requestarr user in their own channel, or an admin in the admin
    // channel (admin requests skip access levels, approval, and quotas).
    public record RequestCaller(RequestarrUser User, bool AsAdmin, Actor Actor, string Name);

    public class TitleState
    {
        public MediaLookupResult Item { get; init; }
        public BackendInstance Instance { get; init; }
        public Request Active { get; init; }
        public string ActiveOwner { get; init; }
        public bool CallerOwnsActive { get; init; }
        public bool CallerFollows { get; init; }
        public Request LastDenied { get; init; }

        // Anime with sub/dub on: the instance's settings, and the language the series is set to.
        public AnimeConfig Anime { get; init; }
        public AnimeLanguage? Language { get; init; }

        // A library series' fix applied while this request was made: its reason, for the new request's card.
        public string AppliedFix { get; set; }
    }

    public record LookupOutcome<T>(T Value, string Error)
    {
        public static LookupOutcome<T> Ok(T value) => new(value, null);

        public static LookupOutcome<T> Fail(string error) => new(default, error);
    }

    // All: every regular season now plus new ones as they air (Future), specials excluded.
    // Otherwise exactly Seasons (0 = specials), plus new seasons when Future is set.
    public record SeasonChoice(bool All, IReadOnlyCollection<int> Seasons, bool Future)
    {
        public static SeasonChoice Everything => new(true, Array.Empty<int>(), true);
    }

    public enum RequestOutcome
    {
        Sent,
        Pending,
        Following,
        AlreadyAvailable,
        Refused,
        Failed
    }

    // A request outcome with the request it created or joined (for Cancel and the final status line).
    // Where a series already in Sonarr really stands (anime replies say this, never "available" early).
    public enum LibraryStanding
    {
        Searching,
        Downloading,
        PartlyAvailable,
        Available
    }

    public record RequestResult(bool Success, string Message, Request Request, RequestOutcome Outcome) : ServiceResult(Success, Message)
    {
        // Replaces the outcome's header on the final card (a series' real state in the library).
        public string Header { get; init; }

        public static RequestResult Refused(string message) => new(false, message, null, RequestOutcome.Refused);

        // The title as looked up, for the card on the final status (null when the lookup failed).
        public MediaLookupResult Item { get; init; }
    }

    public interface IRequestService
    {
        Task<LookupOutcome<List<MediaLookupResult>>> SearchAsync(RequestCaller caller, ContentType type, string query);
        Task<LookupOutcome<TitleState>> DetailAsync(RequestCaller caller, ContentType type, int externalId);
        Task<RequestResult> RequestAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons = null, AnimeLanguage? language = null);
        Task<ServiceResult> ApproveAsync(int requestId, Actor admin, string adminName);
        Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName);
        Task<ServiceResult> CancelAsync(RequestCaller caller, int requestId);
        Task<ServiceResult> AdminCancelAsync(int requestId, string reason, Actor admin, string adminName);

        // Closes a pending request as already available (the title is already there).
        Task<ServiceResult> MarkAvailableAsync(int requestId, Actor admin, string adminName);

        // A passed removal of a history request (Not in library, usually imported): its record leaves Requestarr's lists,
        // with its follows, and the audit log keeps that it was deleted. Nothing happens when it's gone already or isn't history.
        void DeleteRecord(int requestId, Actor actor);

        // What Sonarr / Radarr now say about a sent request. A change (downloading, stuck, failed,
        // seasons available, all there) reposts its messages and feed card; no change does nothing. An imported request's
        // first check only records it.
        Task<bool> ApplyProgressAsync(Request request, ProgressSnapshot snapshot);

        // More seasons for a TV request, by its owner, a follower or an admin: no approval, no quota.
        Task<ServiceResult> AddSeasonsAsync(int requestId, RequestarrUser user, bool asAdmin, Actor actor, SeasonChoice add);

        // Whether the request is one the caller can add seasons to.
        bool CanAddSeasons(Request request, RequestarrUser user, bool asAdmin);

        // A request in trouble: ask its instance to search again (download failed or stuck), or try
        // adding it again (it couldn't be added).
        Task<ServiceResult> SearchAgainAsync(int requestId, Actor admin);
        Task<ServiceResult> RetrySendAsync(int requestId, Actor admin);

        // Everyone on a request (the requester and followers): their access for its type, their limit
        // used, and how many of their requests wait for approval. One line each.
        IReadOnlyList<string> PeopleOn(Request request);

        // Remembers the message a request was made in, so it can be edited after a decision.
        void AttachMessage(int requestId, ulong channelId, ulong messageId);

        // Remembers a follower's own message about a request, reposted when the request changes.
        void AttachFollowerMessage(int requestId, int userId, ulong channelId, ulong messageId);

        // The request's real state as a request message: status line, card, and a Cancel button
        // only while it can still be cancelled.
        Task<InteractionReply> StatusReplyAsync(Request request, MediaLookupResult item = null);

        // The same for someone viewing their own message: a follower's says whose request it is.
        Task<InteractionReply> StatusReplyForAsync(Request request, RequestarrUser viewer, string note = null);

        // The title as the request's instance has it now (null when it can't be looked up).
        Task<MediaLookupResult> ItemForAsync(Request request);

        // A title as an instance has it now, by its TMDB (movie) or TVDB (TV) ID; null when it can't be looked up.
        Task<MediaLookupResult> LookupItemAsync(BackendInstance instance, ContentType type, int externalId);

        // After a removal: the whole title closes the request as removed; seasons leave the request
        // (and close it once none are left). Its people get their messages reposted with the note.
        Task ApplyRemovalAsync(Request request, IReadOnlyCollection<int> seasons, string note);

        // The owner's message edited in place (a removal opened or closed: its Remove button follows).
        Task RefreshOwnerMessageAsync(Request request);
        List<Request> List(RequestarrUser user, bool all, RequestStatus? status, ContentType? type);
        string OwnerName(Request request);
        string DisplayName(int? userId);

        // The admin who decided a request: the member, or the name of an admin in the web app.
        string DecidedBy(Request request);
        string QuotaSummary(RequestarrUser user);
        Request Find(int id);
    }

    public class RequestService : IRequestService
    {
        // The audit log's action for a history request's record leaving with its removal; its detail is the title it had.
        public const string DeleteRecordAction = "request.deleteRecord";

        public const int MaxSearchResults = 10;

        private readonly IRequestRepository _requests;
        private readonly IRequestSubscriberRepository _subscribers;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserMediaAccessRepository _access;
        private readonly IBackendService _backends;
        private readonly IRadarrApi _radarr;
        private readonly ISonarrApi _sonarr;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IAuditService _audit;
        private readonly IMetadataService _metadata;
        private readonly IAnimeService _anime;
        private readonly ISeriesFixService _fixes;
        private readonly IUserIdentityRepository _identities;
        private readonly IRequestFeed _feed;
        private readonly Removals.IRemovalProposalRepository _removals;
        private readonly NzbDrone.Core.Messaging.Events.IEventAggregator _events;
        private readonly Logger _logger;

        public RequestService(IRequestRepository requests,
                              IRequestSubscriberRepository subscribers,
                              IRequestarrUserRepository users,
                              IUserMediaAccessRepository access,
                              IBackendService backends,
                              IRadarrApi radarr,
                              ISonarrApi sonarr,
                              IDiscordGuildGateway gateway,
                              IGuildSetupService guildSetup,
                              IAuditService audit,
                              IMetadataService metadata,
                              IAnimeService anime,
                              ISeriesFixService fixes,
                              IUserIdentityRepository identities,
                              IRequestFeed feed,
                              Removals.IRemovalProposalRepository removals,
                              NzbDrone.Core.Messaging.Events.IEventAggregator events,
                              Logger logger)
        {
            _removals = removals;
            _requests = requests;
            _subscribers = subscribers;
            _users = users;
            _access = access;
            _backends = backends;
            _radarr = radarr;
            _sonarr = sonarr;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _audit = audit;
            _metadata = metadata;
            _anime = anime;
            _fixes = fixes;
            _identities = identities;
            _feed = feed;
            _events = events;
            _logger = logger;
        }

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public Request Find(int id) => _requests.Find(id);

        public async Task<LookupOutcome<List<MediaLookupResult>>> SearchAsync(RequestCaller caller, ContentType type, string query)
        {
            var (instance, error) = Resolve(caller, type);

            if (instance == null)
            {
                return LookupOutcome<List<MediaLookupResult>>.Fail(error);
            }

            try
            {
                var results = type == ContentType.Movie
                    ? await _radarr.LookupAsync(instance.Config, query)
                    : await _sonarr.LookupAsync(instance.Config, query);

                var top = results.Where(r => r.ExternalId(type).HasValue).Take(MaxSearchResults).ToList();

                if (!top.Any())
                {
                    return LookupOutcome<List<MediaLookupResult>>.Fail(DiscordText.NothingFound);
                }

                await _metadata.EnrichAsync(type, top);

                return LookupOutcome<List<MediaLookupResult>>.Ok(top);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Lookup on {0} failed", instance.Name);
                return LookupOutcome<List<MediaLookupResult>>.Fail(DiscordText.RequestsPaused(type));
            }
        }

        public async Task<LookupOutcome<TitleState>> DetailAsync(RequestCaller caller, ContentType type, int externalId)
        {
            var (instance, error) = Resolve(caller, type);

            if (instance == null)
            {
                return LookupOutcome<TitleState>.Fail(error);
            }

            (instance, var item, var problem) = await LookupAtHomeAsync(instance, type, externalId);

            if (problem != null)
            {
                return LookupOutcome<TitleState>.Fail(problem);
            }

            await _metadata.EnrichAsync(type, new[] { item });

            var forTitle = _requests.ForTitle(type, externalId).Where(r => r.BackendId == instance.Id).Newest().ToList();
            var active = forTitle.FirstOrDefault(r => r.IsActive);
            var anime = type == ContentType.Tv && item.IsAnime ? _anime.ConfigFor(instance) : null;
            AnimeLanguage? language = null;

            if (anime is { Enabled: true })
            {
                language = active?.Language ?? (item.LibraryId.HasValue ? _anime.LanguageOf(item.TagIds, await _anime.TagsAsync(instance)) : null);
            }

            return LookupOutcome<TitleState>.Ok(new TitleState
            {
                Item = item,
                Instance = instance,
                Active = active,
                ActiveOwner = active == null ? null : OwnerName(active),
                CallerOwnsActive = active != null && caller.User != null && active.OwnerUserId == caller.User.Id,
                CallerFollows = active != null && caller.User != null && _subscribers.ForRequest(active.Id).Any(s => s.UserId == caller.User.Id),
                LastDenied = active == null ? forTitle.FirstOrDefault(r => r.Status == RequestStatus.Denied) : null,
                Anime = anime is { Enabled: true } ? anime : null,
                Language = language
            });
        }

        // The title as the instance its requests go to has it: looked up, then again there when that's another (anime to
        // the Anime Sonarr). A problem when it isn't found, can't be reached, or its kind is paused (nowhere to go).
        private async Task<(BackendInstance Instance, MediaLookupResult Item, string Problem)> LookupAtHomeAsync(BackendInstance instance, ContentType type, int externalId)
        {
            var at = instance;

            try
            {
                var item = await Lookup(at, type, externalId);
                var home = item == null ? at : Home(type, externalId, item.IsAnime);

                if (home == null)
                {
                    return (at, null, DiscordText.RequestsPausedForNow(type));
                }

                if (home.Id != at.Id)
                {
                    at = home;
                    item = await Lookup(at, type, externalId);
                }

                return (at, item, item == null ? DiscordText.TitleNotFound : null);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Lookup on {0} failed", at.Name);
                return (at, null, DiscordText.RequestsPaused(type));
            }
        }

        // Where a title's requests go: the instance it was last sent to while that one is still in use (its library has
        // it, and anyone else asking follows that request), otherwise the instance for what it is.
        private BackendInstance Home(ContentType type, int externalId, bool isAnime) =>
            _requests.ForTitle(type, externalId).Newest()
                .Select(r => _backends.Find(r.BackendId ?? 0))
                .FirstOrDefault(b => b is { IsDeleted: false, Enable: true })
            ?? _backends.ForRequest(type, isAnime);

        public async Task<RequestResult> RequestAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons = null, AnimeLanguage? language = null)
        {
            var seen = new System.Runtime.CompilerServices.StrongBox<MediaLookupResult>();
            var result = await RequestCoreAsync(caller, type, externalId, seasons, language, seen);

            return result with { Item = seen.Value };
        }

        private async Task<RequestResult> RequestCoreAsync(RequestCaller caller, ContentType type, int externalId, SeasonChoice seasons, AnimeLanguage? language, System.Runtime.CompilerServices.StrongBox<MediaLookupResult> seen)
        {
            if (type == ContentType.Music)
            {
                return RequestResult.Refused(DiscordText.MusicNotYet);
            }

            // Null for admins: no access levels, approval or quota.
            var access = caller.AsAdmin ? null : AccessFor(caller.User, type);

            if (access?.State == AccessState.Off)
            {
                _audit.Record(caller.Actor, "request.refused", "title", $"{type}:{externalId}", "access off");
                return RequestResult.Refused(DiscordText.AccessOff(type));
            }

            var detail = await DetailAsync(caller, type, externalId);

            if (detail.Error != null)
            {
                return RequestResult.Refused(detail.Error);
            }

            var state = detail.Value;
            seen.Value = state.Item;
            await ApplyFixInLibraryAsync(state);

            // Anime: one language per series per instance, settled by the instance's priority rule.
            if (state.Anime == null)
            {
                language = null;
            }
            else
            {
                var existing = await SeriesLanguageAsync(state);
                language ??= AnimeDefaults.Default(state.Anime.Priority, existing);

                if (existing.HasValue && existing != language)
                {
                    return AnimeDefaults.Winner(state.Anime.Priority, existing.Value) == existing.Value
                        ? await StaysAs(caller, state, existing.Value, state.Anime.Priority)
                        : await SwitchTo(caller, state, language.Value);
                }
            }

            if (RequestCards.InLibraryAndDone(state))
            {
                return new RequestResult(true, DiscordText.AlreadyInLibrary, null, RequestOutcome.AlreadyAvailable);
            }

            if (state.Active != null)
            {
                return await JoinActiveAsync(caller, state);
            }

            return await NewRequestAsync(caller, access, type, externalId, state, seasons, language);
        }

        // A listed series already in Sonarr gets its fix too, so it still applies; a request made now shows it.
        private async Task ApplyFixInLibraryAsync(TitleState state)
        {
            if (state.Item.LibraryId is { } seriesId)
            {
                state.AppliedFix = (await ApplyFixAsync(state.Instance, state.Item, seriesId))?.Fix.Reason;
            }
        }

        // Adds a listed library series' fix tag beside its own tags, and keeps the item's tags in step so a language
        // switch after it doesn't drop the fix. A failure there never stops the request.
        private async Task<AppliedFix> ApplyFixAsync(BackendInstance instance, MediaLookupResult item, int seriesId)
        {
            if (_fixes.For(item.TvdbId) == null)
            {
                return null;
            }

            try
            {
                var applied = await _fixes.ApplyToLibraryAsync(instance, item.TvdbId, seriesId, item.TagIds?.ToList() ?? new List<int>());

                if (applied != null && !item.TagIds.Contains(applied.TagId))
                {
                    item.TagIds.Add(applied.TagId);
                }

                return applied;
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Applying the series fix for {0} on {1} failed", item.Title, instance.Name);
                return null;
            }
        }

        // An anime series' language now. In the library, Sonarr's tag is the truth (like any title
        // there, it's good news). Otherwise it's the open request's language.
        private async Task<AnimeLanguage?> SeriesLanguageAsync(TitleState state)
        {
            var tags = await _anime.TagsAsync(state.Instance);

            return state.Item.LibraryId.HasValue ? _anime.LanguageOf(state.Item.TagIds, tags) : state.Active?.Language;
        }

        // Duplicate: attach as subscriber instead of a second request. No quota cost.
        private async Task<RequestResult> JoinActiveAsync(RequestCaller caller, TitleState state)
        {
            if (state.CallerOwnsActive)
            {
                return new RequestResult(false, DiscordText.AlreadyRequestedByYou, state.Active, RequestOutcome.Refused);
            }

            if (state.CallerFollows)
            {
                return new RequestResult(false, DiscordText.AlreadyFollowing, state.Active, RequestOutcome.Following);
            }

            await Subscribe(state.Active, caller.User.Id, state.Item);
            _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString());

            return new RequestResult(true, DiscordText.Subscribed(state.Active.Label, state.ActiveOwner, state.Active.Id), state.Active, RequestOutcome.Following);
        }

        // A title with no open request: a new one, within the caller's limit. Admin requests and
        // auto-approved access are sent at once; the rest wait for an admin.
        private async Task<RequestResult> NewRequestAsync(RequestCaller caller, UserMediaAccess access, ContentType type, int externalId, TitleState state, SeasonChoice seasons, AnimeLanguage? language)
        {
            var item = state.Item;
            var (chosen, future) = type == ContentType.Tv ? ChosenSeasons(item, seasons) : (new List<int>(), false);

            if (type == ContentType.Tv && !chosen.Any() && !future)
            {
                return RequestResult.Refused(DiscordText.ChooseSeasons);
            }

            if (QuotaRefusal(caller, access, type, externalId) is { } overQuota)
            {
                return overQuota;
            }

            var request = _requests.Insert(new Request
            {
                OwnerUserId = caller.User.Id,
                ContentType = type,
                Title = item.Title,
                Year = item.Year,
                TmdbId = type == ContentType.Movie ? externalId : null,
                TvdbId = type == ContentType.Tv ? externalId : null,
                Status = RequestStatus.Pending,
                BackendId = state.Instance.Id,
                Seasons = chosen,
                MonitorFutureSeasons = future,
                Language = language,
                AppliedFix = state.AppliedFix,
                PosterUrl = item.PosterUrl,
                Media = RequestMedia.From(item, Clock()),
                Requested = Clock()
            });

            var languageDetail = language.HasValue ? $", {DiscordText.Label(language.Value)}" : string.Empty;
            var seasonDetail = type == ContentType.Tv ? $", {DiscordText.Seasons(request)}{languageDetail}" : string.Empty;
            _audit.Record(caller.Actor, "request.create", "request", request.Id.ToString(), $"{request.Label} on {state.Instance.Name}{seasonDetail}");

            if (caller.AsAdmin || access.State == AccessState.AutoApprove)
            {
                return await SendNowAsync(caller, request, state);
            }

            await PostApprovalCard(request, state.Instance, item, caller.Name);
            await _feed.PostAsync(request, item);
            _events.PublishEvent(new AdminQueueChangedEvent());

            return new RequestResult(true, DiscordText.RequestPending(request.Label, request.Id), request, RequestOutcome.Pending);
        }

        // The seasons a TV request asks for: All is every season the series has now, otherwise the
        // chosen ones it has (specials only when it has them). No choice is All, plus new seasons
        // while the series continues.
        private static (List<int> Seasons, bool Future) ChosenSeasons(MediaLookupResult item, SeasonChoice choice)
        {
            choice ??= new SeasonChoice(true, Array.Empty<int>(), item.Continuing);
            var chosen = (choice.All ? item.Seasons : choice.Seasons.Where(s => item.Seasons.Contains(s) || (s == 0 && item.HasSpecials))).Distinct().OrderBy(s => s).ToList();

            return (chosen, choice.Future);
        }

        // Over the caller's limit for the type: the refusal (recorded), or null while there's room.
        // Admins have no limit.
        private RequestResult QuotaRefusal(RequestCaller caller, UserMediaAccess access, ContentType type, int externalId)
        {
            if (caller.AsAdmin || !access.QuotaLimit.HasValue)
            {
                return null;
            }

            var window = access.QuotaWindow ?? TimeSpan.FromDays(7);
            var counted = _requests.OwnedBy(caller.User.Id)
                .Where(r => r.ContentType == type && r.IsActive && r.Requested > Clock() - window)
                .OrderBy(r => r.Requested)
                .ToList();

            if (counted.Count < access.QuotaLimit.Value)
            {
                return null;
            }

            var nextSlot = counted[counted.Count - access.QuotaLimit.Value].Requested + window;
            _audit.Record(caller.Actor, "request.refused", "title", $"{type}:{externalId}", $"quota {counted.Count}/{access.QuotaLimit}");
            return RequestResult.Refused(DiscordText.QuotaReached(type, access.QuotaLimit.Value, (int)window.TotalDays, nextSlot));
        }

        // An admin's request, or auto-approved access: straight to the instance.
        private async Task<RequestResult> SendNowAsync(RequestCaller caller, Request request, TitleState state)
        {
            request.DecidedByUserId = caller.AsAdmin ? caller.User.Id : null;
            var (sent, _) = await SendAsync(request, state.Instance, state.Item, caller.AsAdmin ? caller.Actor : Actor.System);

            await _feed.PostAsync(request, state.Item);

            if (request.Status == RequestStatus.Available)
            {
                return new RequestResult(true, DiscordText.RequestAlreadyAvailableStatus(request.Label, request.Id), request, RequestOutcome.AlreadyAvailable);
            }

            return sent
                ? new RequestResult(true, DiscordText.RequestAdded(request.Label, request.Id), request, RequestOutcome.Sent)
                : new RequestResult(false, DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason), request, RequestOutcome.Failed);
        }

        public async Task<ServiceResult> ApproveAsync(int requestId, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.NotPending(request.Id, request.Status));
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            if (instance == null || !instance.Enable)
            {
                return ServiceResult.Fail(Unavailable(instance, request.ContentType));
            }

            var current = await TryLookup(instance, request);

            if (current?.LibraryId != null)
            {
                return await CloseAsAvailable(request, admin, adminName, current);
            }

            SetDecider(request, admin, adminName);
            _audit.Record(admin, "request.approve", "request", request.Id.ToString());

            var (sent, item) = await SendAsync(request, instance, current, admin);

            if (request.Status == RequestStatus.Available)
            {
                await RefreshOrNotify(request, item, DiscordText.RequestAlreadyAvailableNotice(request.Label, request.Id));
                return ServiceResult.Ok(DiscordText.AdminMarkedAvailable(request));
            }

            await RefreshOrNotify(request, item, sent
                ? DiscordText.RequestApprovedNotice(request.Label, request.Id)
                : DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));

            return sent
                ? ServiceResult.Ok(DiscordText.AdminApproved(request, DiscordText.RequestSent(request.Label, request.Id, instance.Name)))
                : ServiceResult.Fail(DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));
        }

        public async Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.NotPending(request.Id, request.Status));
            }

            var inLibrary = await TryLookup(_backends.Find(request.BackendId ?? 0), request);

            if (inLibrary?.LibraryId != null)
            {
                return await CloseAsAvailable(request, admin, adminName, inLibrary);
            }

            request.Status = RequestStatus.Denied;
            request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            request.Decided = Clock();
            SetDecider(request, admin, adminName);
            _requests.Update(request);

            _audit.Record(admin, "request.deny", "request", request.Id.ToString(), request.DecisionReason);
            await RefreshOrNotify(request, null, DiscordText.RequestDeniedNotice(request.Label, request.Id, request.DecisionReason));

            return ServiceResult.Ok(DiscordText.AdminDenied(request));
        }

        public async Task<ServiceResult> AdminCancelAsync(int requestId, string reason, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (!AdminCanCancel(request))
            {
                return ServiceResult.Fail(request.Status == RequestStatus.Approved ? DiscordText.CannotCancelDownloading(request.Id) : DiscordText.CannotCancel(request.Status));
            }

            var instance = _backends.Find(request.BackendId ?? 0);
            var wasSent = request.Status == RequestStatus.Approved && request.ExternalItemId.HasValue;

            if (wasSent && !await TryUnmonitorAsync(request, instance))
            {
                return ServiceResult.Fail(DiscordText.CancelNotStopped(request));
            }

            if (!wasSent && await TryLookup(instance, request) is { LibraryId: not null } available)
            {
                return await CloseAsAvailable(request, admin, adminName, available);
            }

            request.Status = RequestStatus.Cancelled;
            request.DecisionReason = string.IsNullOrWhiteSpace(reason) ? null : reason.Trim();
            request.Decided = Clock();
            SetDecider(request, admin, adminName);
            _requests.Update(request);

            var reasonDetail = request.DecisionReason != null ? "; " + request.DecisionReason : string.Empty;
            _audit.Record(admin, "request.cancel", "request", request.Id.ToString(), wasSent ? $"unmonitored on {instance.Name}{reasonDetail}" : request.DecisionReason);
            await RefreshOrNotify(request, null, DiscordText.RequestCancelledByAdminNotice(request.Label, request.Id, request.DecisionReason));

            return ServiceResult.Ok(wasSent ? DiscordText.AdminCancelledSent(request, instance.Name) : DiscordText.AdminCancelled(request));
        }

        // Already in Sonarr / Radarr: stop the search there, delete nothing. False when it couldn't.
        private async Task<bool> TryUnmonitorAsync(Request request, BackendInstance instance)
        {
            try
            {
                await _backends.ApiFor(instance).SetMonitoredAsync(instance.Config, request.ExternalItemId.Value, false);
                return true;
            }
            catch (Exception ex) when (ex is BackendException || ex is InvalidOperationException || ex is NullReferenceException)
            {
                _logger.Warn(ex, "Request {0}: could not unmonitor it on {1}", request.Id, instance?.Name);
                return false;
            }
        }

        public async Task<ServiceResult> MarkAvailableAsync(int requestId, Actor admin, string adminName)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Pending && !Admin.TroubleSource.InTrouble(request))
            {
                return ServiceResult.Fail(DiscordText.NotPending(request.Id, request.Status));
            }

            return await CloseAsAvailable(request, admin, adminName, null);
        }

        // The title is already there: the request ends as available (never "denied"), the user's
        // message and the approval card say so, and the user is told.
        private async Task<ServiceResult> CloseAsAvailable(Request request, Actor admin, string adminName, MediaLookupResult item)
        {
            request.Status = RequestStatus.Available;
            request.DecisionReason = null;
            request.Decided = Clock();
            request.Fulfilled = Clock();
            SetDecider(request, admin, adminName);
            _requests.Update(request);

            _audit.Record(admin, "request.available", "request", request.Id.ToString(), item?.LibraryId != null ? "already in the library" : "marked by the admin");
            await RefreshOrNotify(request, item, DiscordText.RequestAlreadyAvailableNotice(request.Label, request.Id));

            return ServiceResult.Ok(DiscordText.AdminMarkedAvailable(request));
        }

        // The deciding admin: the member, or by name an admin who isn't one (the web app).
        private static void SetDecider(Request request, Actor admin, string adminName)
        {
            request.DecidedByUserId = admin.UserId;
            request.DecidedByName = admin.UserId.HasValue ? null : adminName;
        }

        public void DeleteRecord(int requestId, Actor actor)
        {
            var request = _requests.Find(requestId);

            if (request?.Status != RequestStatus.NotInLibrary)
            {
                return;
            }

            _subscribers.DeleteMany(_subscribers.ForRequest(requestId));
            _requests.Delete(requestId);
            _audit.Record(actor, DeleteRecordAction, "request", requestId.ToString(), request.Label);
        }

        // The admin can cancel while it waits for approval, or once sent while nothing is
        // downloading or there yet (later, removal is the way: task 4).
        public static bool AdminCanCancel(Request request) =>
            request.Status == RequestStatus.Pending ||
            Admin.TroubleSource.InTrouble(request) ||
            (request.Status == RequestStatus.Approved && request.Progress == DownloadProgress.None && !(request.AvailableSeasons?.Any() ?? false));

        public async Task<bool> ApplyProgressAsync(Request request, ProgressSnapshot snapshot)
        {
            if (snapshot == null || !request.IsTracked)
            {
                return false;
            }

            var wasSoBeforeImport = request.FirstCheckDue;
            request.FirstCheckDue = false;

            if (!RecordProgress(request, snapshot))
            {
                return false;
            }

            if (!wasSoBeforeImport)
            {
                await RefreshOrNotify(request, null, request.Status == RequestStatus.Available
                    ? DiscordText.RequestFulfilledNotice(request.Label, request.Id)
                    : DiscordText.RequestProgressNotice(request));
            }
            else if (Admin.TroubleSource.InTrouble(request))
            {
                _events.PublishEvent(new AdminQueueChangedEvent());
            }

            return true;
        }

        // The request saved as the snapshot says, with what changed in its history; false when nothing did.
        private bool RecordProgress(Request request, ProgressSnapshot snapshot)
        {
            if (snapshot.SizeLeft != request.ProgressSizeLeft)
            {
                request.ProgressSizeLeft = snapshot.SizeLeft;
                request.ProgressSince = Clock();
            }

            var seasons = snapshot.AvailableSeasons.Distinct().OrderBy(s => s).ToList();
            var detail = snapshot.Progress is DownloadProgress.Stalled or DownloadProgress.Failed ? snapshot.Detail : null;
            var changed = snapshot.Complete || snapshot.Progress != request.Progress || !seasons.SequenceEqual(request.AvailableSeasons ?? new List<int>()) || detail != request.ProgressDetail;

            if (!changed)
            {
                _requests.Update(request);
                return false;
            }

            request.AvailableSeasons = seasons;
            request.ProgressDetail = detail;

            if (snapshot.Complete)
            {
                request.Status = RequestStatus.Available;
                request.Progress = DownloadProgress.Imported;
                request.Fulfilled = Clock();
                request.ProgressSizeLeft = null;
                _requests.Update(request);
                _audit.Record(Actor.System, "request.fulfilled", "request", request.Id.ToString(), request.ContentType == ContentType.Tv ? DiscordText.PlainSeasons(seasons) : null);
                return true;
            }

            request.Progress = snapshot.Progress;
            _requests.Update(request);
            _audit.Record(Actor.System, "request.progress", "request", request.Id.ToString(), $"{snapshot.Progress}{(seasons.Any() ? $", {DiscordText.PlainSeasons(seasons)} available" : string.Empty)}{(detail != null ? ": " + detail : string.Empty)}");

            return true;
        }

        public bool CanAddSeasons(Request request, RequestarrUser user, bool asAdmin) =>
            request is { ContentType: ContentType.Tv, IsActive: true } &&
            (asAdmin || (user != null && (request.OwnerUserId == user.Id || _subscribers.ForRequest(request.Id).Any(s => s.UserId == user.Id))));

        public async Task<ServiceResult> AddSeasonsAsync(int requestId, RequestarrUser user, bool asAdmin, Actor actor, SeasonChoice add)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (!CanAddSeasons(request, user, asAdmin))
            {
                return ServiceResult.Fail(request.ContentType != ContentType.Tv || request.IsActive ? DiscordText.NotYourRequest : DiscordText.CannotAddSeasons(request.Status));
            }

            var seasons = (add?.Seasons ?? Array.Empty<int>()).Except(request.Seasons).Distinct().OrderBy(s => s).ToList();
            var future = (add?.Future ?? false) && !request.MonitorFutureSeasons;

            if (!seasons.Any() && !future)
            {
                return ServiceResult.Fail(DiscordText.NothingNewToAdd);
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            // Sent already: Sonarr monitors and searches the new seasons now. Pending: they go with it on approval.
            if (request.Status != RequestStatus.Pending && request.ExternalItemId.HasValue && await AddOnSonarrAsync(request, instance, seasons, future) is { } failed)
            {
                return failed;
            }

            request.Seasons = request.Seasons.Concat(seasons).Distinct().OrderBy(s => s).ToList();
            request.MonitorFutureSeasons |= future;

            if (request.Status == RequestStatus.Available)
            {
                // Fulfilled before: the new seasons are on their way, so it's tracked again.
                request.Status = RequestStatus.Approved;
                request.Progress = DownloadProgress.None;
            }

            _requests.Update(request);

            var added = DiscordText.AddedSeasons(seasons, future);
            _audit.Record(actor, "request.seasons.add", "request", request.Id.ToString(), added);
            await RefreshOrNotify(request, null, DiscordText.SeasonsAddedNotice(request.Label, request.Id, added), skipUserId: user?.Id);

            return ServiceResult.Ok(DiscordText.SeasonsAdded(request, added, request.Status == RequestStatus.Pending));
        }

        // Sonarr monitors and searches the new seasons of a sent request: null when it did, else why not.
        private async Task<ServiceResult> AddOnSonarrAsync(Request request, BackendInstance instance, List<int> seasons, bool future)
        {
            if (instance == null || !instance.Enable)
            {
                return ServiceResult.Fail(Unavailable(instance, ContentType.Tv));
            }

            try
            {
                await _sonarr.AddSeasonsAsync(instance.Config, request.ExternalItemId.Value, seasons, future);
                return null;
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Adding seasons to request {0} on {1} failed", request.Id, instance.Name);
                return ServiceResult.Fail(DiscordText.BackendUnavailable);
            }
        }

        public async Task<ServiceResult> SearchAgainAsync(int requestId, Actor admin)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Approved || !request.ExternalItemId.HasValue)
            {
                return ServiceResult.Fail(DiscordText.NothingToSearch(request));
            }

            var instance = _backends.Find(request.BackendId ?? 0);

            try
            {
                await _backends.ApiFor(instance).SearchAsync(instance.Config, request.ExternalItemId.Value);
            }
            catch (Exception ex) when (ex is BackendException || ex is NullReferenceException)
            {
                _logger.Warn(ex, "Request {0}: search on {1} failed", request.Id, instance?.Name);
                return ServiceResult.Fail(DiscordText.RequestsPaused(request.ContentType));
            }

            _audit.Record(admin, "request.search", "request", request.Id.ToString(), instance.Name);
            return ServiceResult.Ok(DiscordText.SearchingAgain(request, instance.Name));
        }

        public async Task<ServiceResult> RetrySendAsync(int requestId, Actor admin)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (request.Status != RequestStatus.Failed)
            {
                return ServiceResult.Fail(DiscordText.NothingToRetry(request));
            }

            var instance = NewHomeIfDeleted(request) ?? _backends.Find(request.BackendId ?? 0);

            if (instance == null || !instance.Enable)
            {
                return ServiceResult.Fail(Unavailable(instance, request.ContentType));
            }

            _audit.Record(admin, "request.retry", "request", request.Id.ToString());
            request.DecisionReason = null;
            var (sent, item) = await SendAsync(request, instance, null, admin);

            await RefreshOrNotify(request, item, sent
                ? DiscordText.RequestApprovedNotice(request.Label, request.Id)
                : DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));

            return sent || request.Status == RequestStatus.Available
                ? ServiceResult.Ok(DiscordText.RequestSent(request.Label, request.Id, instance.Name))
                : ServiceResult.Fail(DiscordText.RequestFailedNotice(request.Label, request.Id, request.DecisionReason));
        }

        // Why a request's instance can't take it: turned off or deleted pauses its requests; none means none was set up.
        private static string Unavailable(BackendInstance instance, ContentType type) =>
            instance == null ? DiscordText.NoBackend(type) : DiscordText.RequestsPausedForNow(type);

        // A request whose instance was deleted goes where its kind goes on Retry (anime, which has a language, to the
        // Anime Sonarr), as a new title there.
        private BackendInstance NewHomeIfDeleted(Request request)
        {
            if (_backends.Find(request.BackendId ?? 0) is not { IsDeleted: true } || _backends.ForRequest(request.ContentType, request.Language.HasValue) is not { } home)
            {
                return null;
            }

            request.BackendId = home.Id;
            request.ExternalItemId = null;

            return home;
        }

        public IReadOnlyList<string> PeopleOn(Request request)
        {
            var people = new[] { (UserId: request.OwnerUserId, Role: DiscordText.Requester) }
                .Concat(_subscribers.ForRequest(request.Id).Select(s => (s.UserId, Role: DiscordText.Follower)));
            var lines = new List<string>();

            foreach (var (userId, role) in people)
            {
                var user = _users.Find(userId);

                if (user == null)
                {
                    continue;
                }

                var access = AccessFor(user, request.ContentType);
                var window = access.QuotaWindow ?? TimeSpan.FromDays(7);
                var used = _requests.OwnedBy(user.Id).Count(r => r.ContentType == request.ContentType && r.IsActive && r.Requested > Clock() - window);
                var pending = _requests.OwnedBy(user.Id).Count(r => r.Status == RequestStatus.Pending);

                lines.Add(DiscordText.PersonLine(user.DisplayName, role, request.ContentType, access.State, used, access.QuotaLimit, (int)window.TotalDays, pending));
            }

            return lines;
        }

        // A follower joins: no quota cost; the admin feed shows who follows.
        private async Task Subscribe(Request request, int userId, MediaLookupResult item, bool postFeed = true)
        {
            _subscribers.Insert(new RequestSubscriber { RequestId = request.Id, UserId = userId, Added = Clock() });

            if (postFeed)
            {
                await _feed.PostAsync(request, item);
            }
        }

        public void AttachFollowerMessage(int requestId, int userId, ulong channelId, ulong messageId)
        {
            var subscriber = _subscribers.ForRequest(requestId).FirstOrDefault(s => s.UserId == userId);

            if (subscriber == null)
            {
                return;
            }

            subscriber.MessageChannelId = channelId.ToString();
            subscriber.MessageId = messageId.ToString();
            _subscribers.Update(subscriber);
        }

        // The requested language loses to the series' current one: follow it (or, in the library,
        // the good news that it is there).
        private async Task<RequestResult> StaysAs(RequestCaller caller, TitleState state, AnimeLanguage existing, AnimePriority priority)
        {
            var label = Titles.WithYear(state.Item.Title, state.Item.Year);

            if (state.Item.LibraryId.HasValue)
            {
                var standing = await StandingAsync(state);
                var result = await InLibraryResult(caller, state, standing, $"stays {DiscordText.Label(existing)}");

                return result with { Message = DiscordText.AnimeStaysInLibrary(label, existing, priority, standing) };
            }

            if (state.Active == null)
            {
                return new RequestResult(true, DiscordText.AnimeStaysInLibrary(label, existing, priority, LibraryStanding.Available), null, RequestOutcome.AlreadyAvailable);
            }

            if (state.CallerOwnsActive)
            {
                return new RequestResult(false, DiscordText.AnimeStaysYours(label, existing, priority), state.Active, RequestOutcome.Refused);
            }

            if (!state.CallerFollows)
            {
                await Subscribe(state.Active, caller.User.Id, state.Item);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString(), $"asked for {DiscordText.Label(existing == AnimeLanguage.Dub ? AnimeLanguage.Sub : AnimeLanguage.Dub)}, stays {DiscordText.Label(existing)}");
            }

            return new RequestResult(true, DiscordText.AnimeStays(label, existing, priority, state.ActiveOwner, state.Active.Id), state.Active, RequestOutcome.Following);
        }

        // The requested language wins: the series switches. In the library its Sonarr tag changes,
        // and a pending request changes language. Everyone on the request is told; nothing is deleted.
        private async Task<RequestResult> SwitchTo(RequestCaller caller, TitleState state, AnimeLanguage language)
        {
            var item = state.Item;
            var label = Titles.WithYear(item.Title, item.Year);

            try
            {
                if (item.LibraryId.HasValue)
                {
                    await _anime.SetLanguageAsync(state.Instance, item.LibraryId.Value, item.TagIds, language);
                }
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Switching {0} to {1} on {2} failed", label, language, state.Instance.Name);
                return RequestResult.Refused(DiscordText.BackendUnavailable);
            }

            _audit.Record(caller.Actor, "anime.switch", "title", $"tv:{item.TvdbId}", $"{label} on {state.Instance.Name} now {DiscordText.Label(language)}");

            // Someone else's open request: the switcher follows it (joined before the reposts below,
            // so the feed card is posted once, already showing them).
            if (!item.LibraryId.HasValue && state.Active != null && !state.CallerOwnsActive && !state.CallerFollows)
            {
                await Subscribe(state.Active, caller.User.Id, item, postFeed: false);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString(), $"switched it to {DiscordText.Label(language)}");
            }

            // Every request for this title on the instance: its language changes and its people are told.
            foreach (var request in _requests.ForTitle(ContentType.Tv, item.TvdbId.Value).Where(r => r.BackendId == state.Instance.Id && r.IsActive))
            {
                request.Language = language;
                _requests.Update(request);
                await RefreshOrNotify(request, item, DiscordText.AnimeSwitchedNotice(request.Label, language), DiscordText.AnimeSwitchedNote(language), skipUserId: caller.User?.Id);
            }

            if (item.LibraryId.HasValue)
            {
                var standing = await StandingAsync(state);
                var result = await InLibraryResult(caller, state, standing, $"switched it to {DiscordText.Label(language)}");

                return result with { Message = DiscordText.AnimeSwitchedInLibrary(label, language, standing) };
            }

            if (state.Active == null)
            {
                return new RequestResult(true, DiscordText.AnimeSwitchedInLibrary(label, language, LibraryStanding.Available), null, RequestOutcome.AlreadyAvailable);
            }

            // Re-read: the loop above changed its language.
            var active = _requests.Find(state.Active.Id);

            if (state.CallerOwnsActive)
            {
                return new RequestResult(true, DiscordText.AnimeSwitchedYours(label, language), active, RequestOutcome.Pending);
            }

            return new RequestResult(true, DiscordText.AnimeSwitchedFollow(label, language, state.ActiveOwner, active.Id), active, RequestOutcome.Following);
        }

        // A series in Sonarr: its open request's state when there is one, otherwise the episodes on disk.
        private async Task<LibraryStanding> StandingAsync(TitleState state)
        {
            var active = state.Active == null ? null : _requests.Find(state.Active.Id);

            switch (active?.Status)
            {
                case RequestStatus.Available:
                    return LibraryStanding.Available;

                case RequestStatus.Approved when active.Progress is DownloadProgress.Downloading or DownloadProgress.Stalled:
                    return LibraryStanding.Downloading;

                case RequestStatus.Approved:
                    return active.AvailableSeasons?.Any() == true ? LibraryStanding.PartlyAvailable : LibraryStanding.Searching;
            }

            try
            {
                var episodes = await _sonarr.GetEpisodesAsync(state.Instance.Config, state.Item.LibraryId.Value);
                var aired = episodes?.Where(e => e.SeasonNumber > 0 && e.AirDateUtc <= Clock()).ToList();

                if (aired == null)
                {
                    return LibraryStanding.Available;
                }

                // Nothing aired yet counts as searching, like aired episodes without files.
                var withFiles = aired.Count(e => e.HasFile);

                if (withFiles == 0)
                {
                    return LibraryStanding.Searching;
                }

                return withFiles < aired.Count ? LibraryStanding.PartlyAvailable : LibraryStanding.Available;
            }
            catch (BackendException ex)
            {
                _logger.Debug(ex, "Episodes of {0} could not be read", state.Item.Title);
                return LibraryStanding.Available;
            }
        }

        // The reply for a series already in Sonarr: "available" only when it is; while it is still on
        // its way, the switcher follows the open request (or it's theirs).
        private async Task<RequestResult> InLibraryResult(RequestCaller caller, TitleState state, LibraryStanding standing, string why)
        {
            var header = DiscordText.StandingHeader(standing);

            if (standing == LibraryStanding.Available || state.Active == null)
            {
                return new RequestResult(true, null, null, standing == LibraryStanding.Available ? RequestOutcome.AlreadyAvailable : RequestOutcome.Sent) { Header = header };
            }

            if (!state.CallerOwnsActive && !state.CallerFollows && caller.User != null)
            {
                await Subscribe(state.Active, caller.User.Id, state.Item);
                _audit.Record(caller.Actor, "request.subscribe", "request", state.Active.Id.ToString(), why);
            }

            return new RequestResult(true, null, _requests.Find(state.Active.Id), state.CallerOwnsActive ? RequestOutcome.Sent : RequestOutcome.Following) { Header = header };
        }

        public void AttachMessage(int requestId, ulong channelId, ulong messageId)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return;
            }

            request.MessageChannelId = channelId.ToString();
            request.MessageId = messageId.ToString();
            _requests.Update(request);
        }

        public Task<InteractionReply> StatusReplyAsync(Request request, MediaLookupResult item = null) => StatusReplyAsync(request, item, null, null, null);

        public Task<InteractionReply> StatusReplyForAsync(Request request, RequestarrUser viewer, string note = null) =>
            StatusReplyAsync(request, null, null, note, viewer != null && viewer.Id != request.OwnerUserId ? OwnerName(request) : null);

        public Task<MediaLookupResult> ItemForAsync(Request request) => TryLookup(_backends.Find(request.BackendId ?? 0), request);

        public async Task<MediaLookupResult> LookupItemAsync(BackendInstance instance, ContentType type, int externalId)
        {
            try
            {
                var item = await Lookup(instance, type, externalId);

                if (item != null)
                {
                    await _metadata.EnrichAsync(type, new[] { item });
                }

                return item;
            }
            catch (BackendException)
            {
                return null;
            }
        }

        public async Task ApplyRemovalAsync(Request request, IReadOnlyCollection<int> seasons, string note)
        {
            if (seasons == null || seasons.Count == 0)
            {
                request.Status = RequestStatus.Removed;
            }
            else
            {
                request.Seasons = request.Seasons.Except(seasons).ToList();
                request.AvailableSeasons = (request.AvailableSeasons ?? new List<int>()).Except(seasons).ToList();

                if (request.Seasons.Count == 0 && !request.MonitorFutureSeasons)
                {
                    request.Status = RequestStatus.Removed;
                }
            }

            _requests.Update(request);
            _audit.Record(Actor.System, "request.removed", "request", request.Id.ToString(), seasons?.Any() == true ? DiscordText.PlainSeasons(seasons) : "whole title");
            await RefreshMessagesAsync(request, null, ownerRepost: true, note);
        }

        public async Task RefreshOwnerMessageAsync(Request request)
        {
            if (!ulong.TryParse(request.MessageChannelId, out var channel) || !ulong.TryParse(request.MessageId, out var message))
            {
                return;
            }

            try
            {
                var reply = RequestCards.Status(request, null, Mention(request.OwnerUserId), null, null);
                await _gateway.EditMessageAsync(channel, message, WithRemoval(reply, request));
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Request {0}: its message could not be edited", request.Id);
            }
        }

        // The owner's request message: Remove… on a downloaded title with no removal open; a line
        // while one is (the vote itself is in their channel).
        private InteractionReply WithRemoval(InteractionReply reply, Request request)
        {
            var open = _removals.ForRequest(request.Id).FirstOrDefault(p => p.IsOpen);

            if (open != null)
            {
                return new InteractionReply
                {
                    Content = DiscordLimits.Truncate($"{reply.Content}\n{DiscordText.FeedRemovalLine(open, 0, OwnerName(request)).Split(" · ")[0]}", DiscordLimits.MessageLength),
                    Embeds = reply.Embeds,
                    KeepEmbeds = reply.KeepEmbeds,
                    Buttons = reply.Buttons
                };
            }

            if (RequestCards.Downloaded(request))
            {
                reply.Buttons.Add(new InteractionButton { CustomId = $"{Removals.RemovalRoutes.User}:{request.Id}", Label = DiscordText.RemoveRequestLabel, Style = ButtonStyle.Secondary });
            }

            return reply;
        }

        private async Task<InteractionReply> StatusReplyAsync(Request request, MediaLookupResult item, string mention, string note, string followerOf)
        {
            var instance = _backends.Find(request.BackendId ?? 0);
            item ??= await TryLookup(instance, request);
            var reply = RequestCards.Status(request, item, mention, note, followerOf);

            return followerOf == null ? WithRemoval(reply, request) : reply;
        }

        public async Task<ServiceResult> CancelAsync(RequestCaller caller, int requestId)
        {
            var request = _requests.Find(requestId);

            if (request == null)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (caller.User == null || request.OwnerUserId != caller.User.Id)
            {
                return ServiceResult.Fail(DiscordText.NotYourRequest);
            }

            if (request.Status != RequestStatus.Pending)
            {
                return ServiceResult.Fail(DiscordText.CannotCancel(request.Status));
            }

            request.Status = RequestStatus.Cancelled;
            request.Decided = Clock();
            _requests.Update(request);
            _audit.Record(caller.Actor, "request.cancel", "request", request.Id.ToString());
            await RefreshMessagesAsync(request, null, ownerRepost: false, notice: DiscordText.RequestCancelled(request.Label, request.Id));

            return ServiceResult.Ok(DiscordText.RequestCancelled(request.Label, request.Id));
        }

        public List<Request> List(RequestarrUser user, bool all, RequestStatus? status, ContentType? type)
        {
            IEnumerable<Request> requests;

            if (all && user == null)
            {
                requests = _requests.All();
            }
            else
            {
                var followed = _subscribers.ForUser(user.Id).Select(s => s.RequestId).ToHashSet();
                requests = _requests.OwnedBy(user.Id).Concat(_requests.Get(followed));
            }

            return requests
                .Where(r => status == null || r.Status == status)
                .Where(r => type == null || r.ContentType == type)
                .GroupBy(r => r.Id).Select(g => g.First())
                .Newest()
                .ToList();
        }

        public string DisplayName(int? userId) => userId.HasValue ? _users.Find(userId.Value)?.DisplayName : null;

        public string DecidedBy(Request request) => DisplayName(request.DecidedByUserId) ?? request.DecidedByName;

        public string OwnerName(Request request) => _users.Find(request.OwnerUserId)?.DisplayName ?? $"user {request.OwnerUserId}";

        public string QuotaSummary(RequestarrUser user)
        {
            var lines = new List<string> { $"**{user.DisplayName}**" };

            foreach (var type in ContentTypes.Offered)
            {
                var access = AccessFor(user, type);
                var window = access.QuotaWindow ?? TimeSpan.FromDays(7);
                var counted = _requests.OwnedBy(user.Id).Where(r => r.ContentType == type && r.IsActive && r.Requested > Clock() - window).OrderBy(r => r.Requested).ToList();
                DateTime? next = access.QuotaLimit.HasValue && counted.Count >= access.QuotaLimit.Value
                    ? counted[counted.Count - access.QuotaLimit.Value].Requested + window
                    : null;

                lines.Add(DiscordText.QuotaLine(type, access.State, counted.Count, access.QuotaLimit, (int)window.TotalDays, next));
            }

            return string.Join("\n", lines);
        }

        // Adds the title to the instance: its quality profile, root folder, series type,
        // monitored, search started. Records the item ID; failures mark the request Failed.
        private async Task<(bool Sent, MediaLookupResult Item)> SendAsync(Request request, BackendInstance instance, MediaLookupResult item, Actor actor)
        {
            try
            {
                var api = _backends.ApiFor(instance);
                item ??= await Lookup(instance, request.ContentType, request.ExternalId.Value)
                         ?? throw new BackendException(DiscordText.TitleNotFound);

                var profile = BackendService.ResolveProfile(await api.GetQualityProfilesAsync(instance.Config), instance.Config.QualityProfile)
                              ?? throw new BackendException($"its quality profile '{instance.Config.QualityProfile}' isn't there any more");
                var root = instance.Config.RootFolder ?? (await api.GetRootFoldersAsync(instance.Config)).FirstOrDefault()
                           ?? throw new BackendException("there's no root folder to add it to");

                // Anime always goes in as Sonarr's Anime type (absolute numbering, scene mapping). With
                // sub/dub on, it's tagged with its language so the matching release profile applies.
                var seriesType = item.IsAnime ? "anime" : instance.Config.SeriesType;
                var (tags, fix) = await TagsAsync(request, instance, item);

                request.ExternalItemId = request.ContentType == ContentType.Movie
                    ? await _radarr.AddMovieAsync(instance.Config, item, profile.Id, root)
                    : await _sonarr.AddSeriesAsync(instance.Config, item, profile.Id, root, seriesType, request.Seasons, request.MonitorFutureSeasons, tags);

                request.QualityProfileId = profile.Id;
                request.RootFolderPath = root;
                request.Status = RequestStatus.Approved;
                request.Decided = Clock();
                _requests.Update(request);

                var tagDetail = tags.Any() ? $", tags {string.Join(", ", tags)}" : string.Empty;
                var typeDetail = request.ContentType == ContentType.Tv ? $", type {seriesType ?? "standard"}{tagDetail}" : string.Empty;
                _audit.Record(actor, "request.send", "request", request.Id.ToString(), $"{instance.Name} item {request.ExternalItemId}, profile {profile.Name}, root {root}{typeDetail}");

                if (fix != null)
                {
                    _audit.Record(actor, "seriesfix.applied", "request", request.Id.ToString(), $"{fix.Fix.Series} on {instance.Name}: {fix.Fix.Reason}");
                }

                return (true, item);
            }
            catch (BackendException ex) when (AlreadyAdded(ex))
            {
                await AlreadyThereAsync(request, instance, actor);

                return (false, item);
            }
            catch (Exception ex) when (ex is BackendException || ex is InvalidOperationException)
            {
                _logger.Error(ex, "Sending request {0} to {1} failed", request.Id, instance.Name);

                request.Status = RequestStatus.Failed;
                request.DecisionReason = ex.Message;
                request.Decided = Clock();
                _requests.Update(request);

                _audit.Record(actor, "request.failed", "request", request.Id.ToString(), ex.Message);

                return (false, item);
            }
        }

        // The tags a series goes in with: its anime language's, and its series fix's, built in Sonarr now. The request
        // keeps the fix's reason for its card (cleared when it goes in without one, as on a Retry after turning it off).
        private async Task<(List<int> Tags, AppliedFix Fix)> TagsAsync(Request request, BackendInstance instance, MediaLookupResult item)
        {
            var tags = new List<int>();

            if (request.Language.HasValue && await _anime.TagsAsync(instance) is { } animeTags)
            {
                tags.Add(animeTags.For(request.Language.Value));
            }

            var fix = request.ContentType == ContentType.Tv ? await _fixes.BuildAsync(instance, item.TvdbId) : null;
            request.AppliedFix = fix?.Fix.Reason;

            if (fix != null)
            {
                tags.Add(fix.TagId);
            }

            return (tags, fix);
        }

        // Someone added it to the instance since the request was made: it's there, and a listed series gets its fix.
        private async Task AlreadyThereAsync(Request request, BackendInstance instance, Actor actor)
        {
            _logger.Info("Request {0}: {1} already has it", request.Id, instance.Name);

            if (request.ContentType == ContentType.Tv && await TryLookup(instance, request) is { LibraryId: { } seriesId } there)
            {
                request.AppliedFix = (await ApplyFixAsync(instance, there, seriesId))?.Fix.Reason ?? request.AppliedFix;
            }

            request.Status = RequestStatus.Available;
            request.DecisionReason = null;
            request.Decided = Clock();
            request.Fulfilled = Clock();
            _requests.Update(request);
            _audit.Record(actor, "request.available", "request", request.Id.ToString(), $"already on {instance.Name}");
        }

        // Radarr / Sonarr reject a second add with "This movie / series has already been added".
        private static bool AlreadyAdded(BackendException ex) => ex.Message.Contains("already been added", StringComparison.OrdinalIgnoreCase);

        private async Task PostApprovalCard(Request request, BackendInstance instance, MediaLookupResult item, string ownerName)
        {
            var adminChannel = ulong.Parse(_guildSetup.GetConfig().AdminChannelId);

            var id = await _gateway.SendMessageAsync(adminChannel, RequestCards.Approval(request, ownerName, instance.Name, item));
            request.AdminMessageId = id.ToString();
            _requests.Update(request);
        }

        // After a request changes while its people are no longer in the request flow, each of them
        // gets the news as the newest message in their channel: their old message about it is
        // deleted and a fresh one, mentioning them, is posted with only the buttons valid now. The
        // owner's own Cancel click edits in place instead (ownerRepost false). The admin card is
        // edited in place. Returns whether the owner's message was handled (else: notice fallback).
        private async Task<bool> RefreshMessagesAsync(Request request, MediaLookupResult item = null, bool ownerRepost = true, string note = null, int? skipUserId = null, string notice = null)
        {
            var instance = _backends.Find(request.BackendId ?? 0);
            item ??= await TryLookup(instance, request);
            var handled = false;

            if (request.OwnerUserId != skipUserId && ulong.TryParse(request.MessageChannelId, out var channel) && ulong.TryParse(request.MessageId, out var message))
            {
                try
                {
                    var reply = WithRemoval(RequestCards.Status(request, item, ownerRepost ? Mention(request.OwnerUserId) : null, note, null), request);

                    if (ownerRepost)
                    {
                        request.MessageId = (await Repost(channel, message, reply)).ToString();
                        _requests.Update(request);
                        handled = true;
                    }
                    else
                    {
                        handled = await _gateway.EditMessageAsync(channel, message, reply);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Request {0}: its message could not be updated", request.Id);
                }
            }
            else if (request.OwnerUserId == skipUserId)
            {
                handled = true;
            }

            await RefreshFollowers(request, item, note, skipUserId, notice);
            await _feed.PostAsync(request, item);
            _events.PublishEvent(new AdminQueueChangedEvent());

            if (ulong.TryParse(request.AdminMessageId, out var card) && ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var adminChannel))
            {
                try
                {
                    var decidedBy = DecidedBy(request);
                    await _gateway.EditMessageAsync(adminChannel, card, new InteractionReply { Content = DiscordText.DecidedCard(request, OwnerName(request), decidedBy), KeepEmbeds = true });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Request {0}: its approval card could not be edited", request.Id);
                }
            }

            return handled;
        }

        private async Task RefreshFollowers(Request request, MediaLookupResult item, string note, int? skipUserId, string notice)
        {
            var owner = OwnerName(request);

            foreach (var follower in _subscribers.ForRequest(request.Id).Where(f => f.UserId != skipUserId))
            {
                if (ulong.TryParse(follower.MessageChannelId, out var channel) && ulong.TryParse(follower.MessageId, out var message))
                {
                    try
                    {
                        var reply = RequestCards.Status(request, item, Mention(follower.UserId), note, owner);
                        follower.MessageId = (await Repost(channel, message, reply)).ToString();
                        _subscribers.Update(follower);
                        continue;
                    }
                    catch (Exception ex)
                    {
                        _logger.Debug(ex, "Request {0}: follower {1}'s message could not be reposted", request.Id, follower.UserId);
                    }
                }

                if (notice != null)
                {
                    await NotifyUser(follower.UserId, notice);
                }
            }
        }

        // Deletes the old message and posts the new one at the bottom. If the old one can't be
        // deleted, the new one is posted anyway and the old one loses its buttons.
        private async Task<ulong> Repost(ulong channel, ulong old, InteractionReply reply)
        {
            var deleted = false;

            try
            {
                deleted = await _gateway.DeleteMessageAsync(channel, old);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Could not delete message {0}", old);
            }

            if (!deleted)
            {
                try
                {
                    await _gateway.EditMessageAsync(channel, old, new InteractionReply { Content = reply.Content, KeepEmbeds = true });
                }
                catch (Exception ex)
                {
                    _logger.Debug(ex, "Could not strip the buttons from message {0}", old);
                }
            }

            return await _gateway.SendMessageAsync(channel, reply);
        }

        private string Mention(int userId)
        {
            var discordId = _identities.GetForUser(userId).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;
            return discordId == null ? null : $"<@{discordId}>";
        }

        // The request's messages show the outcome; a separate notice only to whoever has no message
        // to show it (requests from before messages were stored, or a message that is gone).
        private async Task RefreshOrNotify(Request request, MediaLookupResult item, string notice, string note = null, int? skipUserId = null)
        {
            if (!await RefreshMessagesAsync(request, item, ownerRepost: true, note, skipUserId, notice))
            {
                await SendToMemberAsync(request.OwnerUserId, notice);
            }
        }

        private Task NotifyUser(int userId, string message) => SendToMemberAsync(userId, $"{Mention(userId)} {message}".Trim());

        // Only ever in their own channel: someone who isn't onboarded is never messaged, here or anywhere else.
        private async Task SendToMemberAsync(int userId, string message)
        {
            var user = _users.Find(userId);

            if (user?.Status != UserStatus.Active || !ulong.TryParse(user.PrivateChannelId, out var channel))
            {
                return;
            }

            try
            {
                await _gateway.SendMessageAsync(channel, new InteractionReply { Content = message });
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not notify user {0}", userId);
            }
        }

        private async Task<MediaLookupResult> TryLookup(BackendInstance instance, Request request)
        {
            if (instance == null || !request.ExternalId.HasValue)
            {
                return null;
            }

            try
            {
                var item = await Lookup(instance, request.ContentType, request.ExternalId.Value);

                if (item != null)
                {
                    await _metadata.EnrichAsync(request.ContentType, new[] { item });
                }

                return item;
            }
            catch (BackendException)
            {
                return null;
            }
        }

        private Task<MediaLookupResult> Lookup(BackendInstance instance, ContentType type, int externalId)
        {
            return type == ContentType.Movie
                ? _radarr.LookupTmdbAsync(instance.Config, externalId)
                : _sonarr.LookupTvdbAsync(instance.Config, externalId);
        }

        private (BackendInstance Instance, string Error) Resolve(RequestCaller caller, ContentType type)
        {
            if (type == ContentType.Music)
            {
                return (null, DiscordText.MusicNotYet);
            }

            // A type with no instance that has ever connected isn't shown to users, so it can't be requested.
            if (!caller.AsAdmin && !_backends.EverConnected(type))
            {
                return (null, DiscordText.TypeNotAvailable(type));
            }

            // Where a request goes follows what is asked for (DetailAsync routes anime), never who asks.
            var instance = _backends.Default(type);

            return instance == null ? (null, DiscordText.NoBackend(type)) : (instance, null);
        }

        private UserMediaAccess AccessFor(RequestarrUser user, ContentType type)
        {
            return _access.GetForUser(user.Id).FirstOrDefault(a => a.ContentType == type) ??
                   new UserMediaAccess { UserId = user.Id, ContentType = type, State = _backends.DefaultAccess(type) };
        }
    }

    public static class MediaLookupExtensions
    {
        public static int? ExternalId(this MediaLookupResult result, ContentType type) => type == ContentType.Tv ? result.TvdbId : result.TmdbId;
    }
}
