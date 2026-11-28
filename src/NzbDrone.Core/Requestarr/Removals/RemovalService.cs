using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using NzbDrone.Core.Messaging.Commands;
using NzbDrone.Core.Messaging.Events;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Removals
{
    // Every minute: reminders, requester timeouts, closing votes, and prompts for users who gained access.
    public class RemovalCheckCommand : Command
    {
        public override bool SendUpdatesToClient => false;
    }

    // A title that can be removed: where it is, what is on disk, and its open request and requester
    // (null when it was never requested through Requestarr, or the requester was removed).
    public record RemovalTarget(BackendInstance Instance, LibraryTitle Title, Request Request, RequestarrUser Requester)
    {
        public ContentType Type => DiscordText.TypeOf(Instance);

        public string Label => Titles.WithYear(Title.Title, Title.Year);
    }

    public interface IRemovalService
    {
        RemovalProposal Find(int id);

        // The proposal the request's cards show: an open one, else the latest.
        RemovalProposal LatestFor(int requestId);

        IReadOnlyList<RemovalFlag> FlagsOn(int proposalId);

        Task<LookupOutcome<RemovalTarget>> TargetAsync(int backendId, int itemId);
        Task<LookupOutcome<RemovalTarget>> TargetForRequestAsync(int requestId);

        // The library of every connected Sonarr and Radarr, by title (titles nobody requested too).
        // unrequestedOnly: only titles without an active request here, so the first matches aren't all requested ones.
        Task<List<RemovalTarget>> SearchLibraryAsync(string term, bool unrequestedOnly = false);

        // Requests whose title Sonarr and Radarr don't have (Not in library) matching the search, as the web app's
        // header search finds them: their removal only leaves Requestarr's lists.
        List<Request> SearchHistory(string term);

        // User-initiated: the requester removes their own title (or seasons), straight to the vote.
        Task<ServiceResult> StartByUserAsync(RequestarrUser user, Actor actor, int requestId, IReadOnlyCollection<int> seasons);

        // Admin-initiated: an owned title asks the requester first, an ownerless one goes to the vote,
        // and force deletes now.
        Task<ServiceResult> StartByAdminAsync(Actor admin, string adminName, int? adminUserId, int backendId, int itemId, IReadOnlyCollection<int> seasons, bool force);

        // The same, for a request Sonarr and Radarr don't have (IsRecordOnly): the whole request (seasons must be empty),
        // and passing drops it from Requestarr's lists instead of deleting anything.
        Task<ServiceResult> StartRecordByAdminAsync(Actor admin, string adminName, int? adminUserId, int requestId, IReadOnlyCollection<int> seasons, bool force);

        Task<ServiceResult> AnswerAsync(int proposalId, RequestarrUser user, Actor actor, bool yes);
        Task<ServiceResult> SetKeepAsync(int proposalId, RequestarrUser user, Actor actor, bool keep);
        Task<ServiceResult> ForceAsync(int proposalId, Actor admin, string adminName, int? adminUserId);
        Task<ServiceResult> DropAsync(int proposalId, Actor admin, string adminName, int? adminUserId);

        // An agreed removal waiting while deleting files is off: the admin deleted it by hand.
        Task<ServiceResult> DeletedByHandAsync(int proposalId, Actor admin, string adminName, int? adminUserId);

        Task TickAsync();

        // Whether removals may delete in Sonarr or Radarr on this server (IRemovalSettings.DeleteFiles).
        bool DeletesFiles { get; }
    }

    public class RemovalService : IRemovalService, IExecute<RemovalCheckCommand>
    {
        private readonly IRemovalProposalRepository _proposals;
        private readonly IRemovalFlagRepository _flags;
        private readonly IRemovalPromptRepository _prompts;
        private readonly IRequestRepository _requests;
        private readonly IRequestarrUserRepository _users;
        private readonly IUserMediaAccessRepository _access;
        private readonly IUserIdentityRepository _identities;
        private readonly IBackendService _backends;
        private readonly IRequestService _requestService;
        private readonly IRequestFeed _feed;
        private readonly IDiscordGuildGateway _gateway;
        private readonly IGuildSetupService _guildSetup;
        private readonly IAuditService _audit;
        private readonly IRemovalSettings _settings;
        private readonly IEventAggregator _events;
        private readonly Logger _logger;

        public RemovalService(IRemovalProposalRepository proposals,
                              IRemovalFlagRepository flags,
                              IRemovalPromptRepository prompts,
                              IRequestRepository requests,
                              IRequestarrUserRepository users,
                              IUserMediaAccessRepository access,
                              IUserIdentityRepository identities,
                              IBackendService backends,
                              IRequestService requestService,
                              IRequestFeed feed,
                              IDiscordGuildGateway gateway,
                              IGuildSetupService guildSetup,
                              IAuditService audit,
                              IRemovalSettings settings,
                              IEventAggregator events,
                              Logger logger)
        {
            _proposals = proposals;
            _flags = flags;
            _prompts = prompts;
            _requests = requests;
            _users = users;
            _access = access;
            _identities = identities;
            _backends = backends;
            _requestService = requestService;
            _feed = feed;
            _gateway = gateway;
            _guildSetup = guildSetup;
            _audit = audit;
            _settings = settings;
            _events = events;
            _logger = logger;
        }

        private bool? _fastVotesNoticed;

        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public void Execute(RemovalCheckCommand message) => TickAsync().GetAwaiter().GetResult();

        public RemovalProposal Find(int id) => _proposals.Find(id);

        public RemovalProposal LatestFor(int requestId)
        {
            var all = _proposals.ForRequest(requestId);
            return all.Where(p => p.IsOpen).OrderByDescending(p => p.Id).FirstOrDefault() ?? all.OrderByDescending(p => p.Id).FirstOrDefault();
        }

        public IReadOnlyList<RemovalFlag> FlagsOn(int proposalId) => _flags.ForProposal(proposalId);

        public async Task<LookupOutcome<RemovalTarget>> TargetAsync(int backendId, int itemId)
        {
            var instance = _backends.Find(backendId);

            if (instance == null || instance.Implementation is not (BackendImplementations.Sonarr or BackendImplementations.Radarr))
            {
                return LookupOutcome<RemovalTarget>.Fail(DiscordText.TitleNotFound);
            }

            List<LibraryTitle> library;

            try
            {
                library = await _backends.ApiFor(instance).GetLibraryAsync(instance.Config);
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Reading {0}'s library failed", instance.Name);
                return LookupOutcome<RemovalTarget>.Fail(DiscordText.RequestsPaused(DiscordText.TypeOf(instance)));
            }

            var title = library.FirstOrDefault(t => t.Id == itemId);
            return title == null ? LookupOutcome<RemovalTarget>.Fail(DiscordText.RemovalNotInLibrary) : LookupOutcome<RemovalTarget>.Ok(TargetFor(instance, title));
        }

        public async Task<LookupOutcome<RemovalTarget>> TargetForRequestAsync(int requestId)
        {
            var request = _requests.Find(requestId);

            if (request?.BackendId == null || !request.ExternalItemId.HasValue)
            {
                return LookupOutcome<RemovalTarget>.Fail(DiscordText.RemovalNotInLibrary);
            }

            return await TargetAsync(request.BackendId.Value, request.ExternalItemId.Value);
        }

        public async Task<List<RemovalTarget>> SearchLibraryAsync(string term, bool unrequestedOnly = false)
        {
            var found = new List<RemovalTarget>();
            term = (term ?? string.Empty).Trim();

            foreach (var instance in _backends.All().Where(b => b.Enable && b.EverConnected && b.Implementation is BackendImplementations.Sonarr or BackendImplementations.Radarr))
            {
                try
                {
                    var library = await _backends.ApiFor(instance).GetLibraryAsync(instance.Config);
                    found.AddRange(library.Where(t => t.Title != null && t.Title.Contains(term, StringComparison.OrdinalIgnoreCase)).Select(t => TargetFor(instance, t)));
                }
                catch (BackendException ex)
                {
                    _logger.Warn(ex, "Searching {0}'s library failed", instance.Name);
                }
            }

            return found.Where(t => !unrequestedOnly || t.Request == null).OrderBy(t => t.Title.Title, StringComparer.OrdinalIgnoreCase).ThenBy(t => t.Instance.Name).Take(DiscordLimits.ChoicesPerMenu).ToList();
        }

        public List<Request> SearchHistory(string term)
        {
            term = (term ?? string.Empty).Trim();

            return _requests.All()
                .Where(r => IsRecordOnly(r) && r.Title != null && r.Title.Contains(term, StringComparison.OrdinalIgnoreCase))
                .OrderBy(r => r.Title, StringComparer.OrdinalIgnoreCase)
                .Take(DiscordLimits.ChoicesPerMenu)
                .ToList();
        }

        public async Task<ServiceResult> StartByUserAsync(RequestarrUser user, Actor actor, int requestId, IReadOnlyCollection<int> seasons)
        {
            var request = _requests.Find(requestId);

            if (request == null || user == null || request.OwnerUserId != user.Id)
            {
                return ServiceResult.Fail(DiscordText.RemovalOnlyYours);
            }

            var target = await TargetForRequestAsync(requestId);

            if (target.Error != null)
            {
                return ServiceResult.Fail(target.Error);
            }

            var proposal = NewProposal(target.Value, seasons, out var error);

            if (proposal == null)
            {
                return ServiceResult.Fail(error);
            }

            proposal.InitiatedByUserId = user.Id;
            proposal.InitiatedByName = user.DisplayName;
            proposal.RequesterUserId = user.Id;
            _proposals.Insert(proposal);
            _audit.Record(actor, "removal.start", "removal", proposal.Id.ToString(), $"{Scope(proposal)} on {target.Value.Instance.Name}, by its requester");

            // Their own channel says it's up for a vote, and gets the outcome.
            var message = await PostToUser(user.Id, DiscordText.RemovalStartedByYou(proposal, Closes()));
            proposal.RequesterChannelId = message.Channel;
            proposal.RequesterMessageId = message.Id;

            await StartVoting(proposal);
            return ServiceResult.Ok(DiscordText.RemovalVoteStarted(proposal));
        }

        public async Task<ServiceResult> StartByAdminAsync(Actor admin, string adminName, int? adminUserId, int backendId, int itemId, IReadOnlyCollection<int> seasons, bool force)
        {
            var target = await TargetAsync(backendId, itemId);

            if (target.Error != null)
            {
                return ServiceResult.Fail(target.Error);
            }

            var proposal = NewProposal(target.Value, seasons, out var error);

            return proposal == null
                ? ServiceResult.Fail(error)
                : await StartAsAdmin(proposal, target.Value.Requester, $"on {target.Value.Instance.Name}", admin, adminName, adminUserId, force);
        }

        public async Task<ServiceResult> StartRecordByAdminAsync(Actor admin, string adminName, int? adminUserId, int requestId, IReadOnlyCollection<int> seasons, bool force)
        {
            var request = _requests.Find(requestId);

            if (request == null || !IsRecordOnly(request))
            {
                return ServiceResult.Fail(request == null ? DiscordText.RequestNotFound : DiscordText.RemovalNotHistory);
            }

            var proposal = NewRecordProposal(request, seasons, out var error);

            return proposal == null
                ? ServiceResult.Fail(error)
                : await StartAsAdmin(proposal, AskableRequester(request), "in Requestarr only (it isn't in the library)", admin, adminName, adminUserId, force);
        }

        // A request Sonarr and Radarr don't have (history, usually imported): its removal drops only its record.
        public static bool IsRecordOnly(Request request) => request.Status == RequestStatus.NotInLibrary;

        // where: what it's removed from, for the audit log. An owned title asks the requester first, an ownerless one goes
        // to the vote, and force removes it now.
        private async Task<ServiceResult> StartAsAdmin(RemovalProposal proposal, RequestarrUser requester, string where, Actor admin, string adminName, int? adminUserId, bool force)
        {
            proposal.InitiatedByAdmin = true;
            proposal.InitiatedByUserId = adminUserId;
            proposal.InitiatedByName = adminName;
            proposal.RequesterUserId = requester?.Id;
            _proposals.Insert(proposal);
            _audit.Record(admin, "removal.start", "removal", proposal.Id.ToString(), $"{Scope(proposal)} {where}, by the admin{(force ? ", forced" : string.Empty)}");

            if (force)
            {
                return await DeleteNow(proposal, admin, adminName, adminUserId);
            }

            if (requester != null)
            {
                proposal.Status = RemovalStatus.AwaitingRequester;
                proposal.AnswerBy = Clock() + _settings.RequesterTimeout;
                var message = await PostToUser(proposal.RequesterUserId.Value, DiscordText.RemovalAskRequester(proposal, proposal.AnswerBy.Value, ReminderAt(proposal)), AnswerButtons(proposal), await Card(proposal));
                proposal.RequesterChannelId = message.Channel;
                proposal.RequesterMessageId = message.Id;
                Save(proposal);
                await Changed(proposal);

                return ServiceResult.Ok(DiscordText.RemovalAskedRequester(requester.DisplayName, proposal.AnswerBy.Value));
            }

            // Never requested through Requestarr, or the requester was removed: straight to the vote.
            await StartVoting(proposal);
            return ServiceResult.Ok(DiscordText.RemovalVoteStarted(proposal));
        }

        public async Task<ServiceResult> AnswerAsync(int proposalId, RequestarrUser user, Actor actor, bool yes)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal == null || user == null || proposal.RequesterUserId != user.Id)
            {
                return ServiceResult.Fail(DiscordText.RemovalNotYours);
            }

            if (proposal.Status != RemovalStatus.AwaitingRequester)
            {
                return ServiceResult.Fail(DiscordText.RemovalAlreadyMoved(proposal));
            }

            proposal.RequesterResponse = yes ? RequesterResponse.Yes : RequesterResponse.No;
            proposal.Answered = Clock();
            _audit.Record(actor, yes ? "removal.requester-yes" : "removal.requester-no", "removal", proposal.Id.ToString(), Scope(proposal));

            if (!yes)
            {
                proposal.Status = RemovalStatus.Declined;
                Save(proposal);
                await EditRequesterMessage(proposal, DiscordText.RemovalYouSaidNo(proposal));
                await Changed(proposal);
                return ServiceResult.Ok(DiscordText.RemovalYouSaidNo(proposal));
            }

            await StartVoting(proposal);
            await EditRequesterMessage(proposal, DiscordText.RemovalYouSaidYes(proposal, proposal.WindowClosesAt.Value));
            return ServiceResult.Ok(DiscordText.RemovalYouSaidYes(proposal, proposal.WindowClosesAt.Value));
        }

        public async Task<ServiceResult> SetKeepAsync(int proposalId, RequestarrUser user, Actor actor, bool keep)
        {
            var proposal = _proposals.Find(proposalId);
            var prompt = proposal == null || user == null ? null : _prompts.ForProposal(proposal.Id).FirstOrDefault(p => p.UserId == user.Id);

            if (prompt == null)
            {
                return ServiceResult.Fail(DiscordText.RemovalNotYours);
            }

            if (proposal.Status != RemovalStatus.Voting)
            {
                return ServiceResult.Fail(DiscordText.RemovalVoteClosed(proposal));
            }

            var existing = _flags.ForProposal(proposal.Id).FirstOrDefault(f => f.UserId == user.Id);

            if (keep && existing == null)
            {
                _flags.Insert(new RemovalFlag { ProposalId = proposal.Id, UserId = user.Id, FlaggedAt = Clock() });
                _audit.Record(actor, "removal.keep", "removal", proposal.Id.ToString(), Scope(proposal));
            }
            else if (!keep && existing != null)
            {
                _flags.Delete(existing);
                _audit.Record(actor, "removal.keep-withdrawn", "removal", proposal.Id.ToString(), Scope(proposal));
            }

            await _feedOrCard(proposal);
            return ServiceResult.Ok(keep ? DiscordText.RemovalKeepNoted : DiscordText.RemovalKeepTakenBack);
        }

        public async Task<ServiceResult> ForceAsync(int proposalId, Actor admin, string adminName, int? adminUserId)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal == null || !proposal.IsOpen)
            {
                return ServiceResult.Fail(proposal == null ? DiscordText.RemovalNotFound : DiscordText.RemovalAlreadyMoved(proposal));
            }

            if (proposal.Status == RemovalStatus.DeleteByHand && !_settings.DeleteFiles)
            {
                return ServiceResult.Fail(DiscordText.RemovalAlreadyByHand(proposal));
            }

            _audit.Record(admin, "removal.force", "removal", proposal.Id.ToString(), $"{Scope(proposal)} forced by {adminName} at {Clock():u} (was {proposal.Status}, {_flags.ForProposal(proposal.Id).Count} keep flag(s))");
            return await DeleteNow(proposal, admin, adminName, adminUserId);
        }

        public async Task<ServiceResult> DropAsync(int proposalId, Actor admin, string adminName, int? adminUserId)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal == null || !proposal.IsOpen)
            {
                return ServiceResult.Fail(proposal == null ? DiscordText.RemovalNotFound : DiscordText.RemovalAlreadyMoved(proposal));
            }

            // Users were already told it's kept when the requester said no or the vote ended with flags.
            var toldAlready = proposal.Status is RemovalStatus.Declined or RemovalStatus.Flagged;
            proposal.Status = proposal.Status == RemovalStatus.Flagged ? RemovalStatus.Kept : RemovalStatus.Cancelled;
            proposal.Closed = Clock();
            proposal.ClosedByUserId = adminUserId;
            proposal.ClosedByName = adminName;
            Save(proposal);
            _audit.Record(admin, "removal.drop", "removal", proposal.Id.ToString(), $"{Scope(proposal)} dropped by {adminName}");

            if (!toldAlready)
            {
                await PostResults(proposal);
            }

            await Changed(proposal);
            return ServiceResult.Ok(DiscordText.RemovalDropped(proposal));
        }

        public async Task TickAsync()
        {
            await NoticeFastVotes();
            var now = Clock();

            foreach (var proposal in _proposals.WithStatus(RemovalStatus.AwaitingRequester))
            {
                if (proposal.AnswerBy <= now)
                {
                    // No answer: counts as yes, as the prompt said. They can still flag Keep it in the vote.
                    proposal.RequesterResponse = RequesterResponse.Silent;
                    proposal.Answered = now;
                    _audit.Record(Actor.System, "removal.requester-silent", "removal", proposal.Id.ToString(), Scope(proposal));
                    await StartVoting(proposal);
                    await EditRequesterMessage(proposal, DiscordText.RemovalNoAnswer(proposal, proposal.WindowClosesAt.Value));
                }
                else if (!proposal.ReminderSent && ReminderAt(proposal) is { } remind && remind <= now)
                {
                    proposal.ReminderSent = true;
                    var text = DiscordText.RemovalReminder(proposal, proposal.AnswerBy.Value);
                    var message = await Repost(proposal.RequesterChannelId, proposal.RequesterMessageId, proposal.RequesterUserId.Value, text, AnswerButtons(proposal), await Card(proposal));
                    proposal.RequesterMessageId = message ?? proposal.RequesterMessageId;
                    Save(proposal);
                }
            }

            foreach (var proposal in _proposals.WithStatus(RemovalStatus.Voting))
            {
                if (proposal.WindowClosesAt <= now)
                {
                    await CloseVote(proposal);
                }
                else
                {
                    await SyncPrompts(proposal);
                }
            }
        }

        // Dev fast votes: the admin channel says when they go on or off, so nobody takes minute-long
        // votes for the real settings. Checked with every removal check (every minute).
        private async Task NoticeFastVotes()
        {
            var on = _settings.FastVotes;

            if (_fastVotesNoticed == on || (_fastVotesNoticed == null && !on))
            {
                _fastVotesNoticed = on;
                return;
            }

            _fastVotesNoticed = on;
            _logger.Info(on ? "Dev fast votes are on: removal timings are in minutes" : "Dev fast votes are off");

            if (ulong.TryParse(_guildSetup.GetConfig()?.AdminChannelId, out var channel))
            {
                try
                {
                    var text = on
                        ? DiscordText.FastVotesOn(_settings.FastVoteTimings, _settings.VoteDays, _settings.RequesterTimeoutDays, _settings.ReminderDay)
                        : DiscordText.FastVotesOff(_settings.VoteDays, _settings.RequesterTimeoutDays, _settings.ReminderDay);
                    await _gateway.SendMessageAsync(channel, new InteractionReply { Content = text });
                }
                catch (Exception ex)
                {
                    _logger.Warn(ex, "Could not post the fast votes notice");
                }
            }
        }

        private RemovalTarget TargetFor(BackendInstance instance, LibraryTitle title)
        {
            var type = DiscordText.TypeOf(instance);
            var externalId = type == ContentType.Tv ? title.TvdbId : title.TmdbId;
            var request = externalId.HasValue
                ? _requests.ForTitle(type, externalId.Value).Where(r => r.BackendId == instance.Id && r.IsActive).Newest().FirstOrDefault()
                : null;

            return new RemovalTarget(instance, title, request, AskableRequester(request));
        }

        // Only a member can be asked first: someone removed, or known only from an import, has nowhere to be asked.
        private RequestarrUser AskableRequester(Request request)
        {
            var requester = request == null ? null : _users.Find(request.OwnerUserId);
            return requester is not null && requester.Status != UserStatus.Removed && !requester.IsImported ? requester : null;
        }

        private RemovalProposal NewProposal(RemovalTarget target, IReadOnlyCollection<int> seasons, out string error)
        {
            var scope = target.Type == ContentType.Tv ? (seasons ?? Array.Empty<int>()).Distinct().OrderBy(s => s).ToList() : new List<int>();
            error = null;

            if (scope.Except(target.Title.Seasons).Any())
            {
                error = DiscordText.RemovalNoSuchSeason;
                return null;
            }

            // One open removal per title and season scope: the whole title overlaps everything, and
            // seasons overlap when they share one; different seasons can run side by side.
            var open = _proposals.WithStatus(RemovalStatus.AwaitingRequester, RemovalStatus.Voting, RemovalStatus.Declined, RemovalStatus.Flagged, RemovalStatus.Failed, RemovalStatus.DeleteByHand)
                .FirstOrDefault(p => p.Overlaps(target.Instance.Id, target.Title.Id, scope));

            if (open != null)
            {
                error = DiscordText.RemovalAlreadyOpen(open);
                return null;
            }

            return new RemovalProposal
            {
                RequestId = target.Request?.Id,
                ContentType = target.Type,
                Title = target.Title.Title,
                Year = target.Title.Year,
                TmdbId = target.Title.TmdbId,
                TvdbId = target.Title.TvdbId,
                PosterUrl = target.Title.PosterUrl ?? target.Request?.PosterUrl,
                BackendId = target.Instance.Id,
                ExternalItemId = target.Title.Id,
                Seasons = scope,
                Status = RemovalStatus.Voting,
                Created = Clock()
            };
        }

        // No library item and no seasons to pick: always the whole request, one open removal at a time.
        private RemovalProposal NewRecordProposal(Request request, IReadOnlyCollection<int> seasons, out string error)
        {
            error = null;

            if (seasons?.Count > 0)
            {
                error = DiscordText.RemovalRecordIsWhole;
                return null;
            }

            if (_proposals.ForRequest(request.Id).FirstOrDefault(p => p.IsOpen) is { } open)
            {
                error = DiscordText.RemovalAlreadyOpen(open);
                return null;
            }

            return new RemovalProposal
            {
                RequestId = request.Id,
                ContentType = request.ContentType,
                Title = request.Title,
                Year = request.Year,
                TmdbId = request.TmdbId,
                TvdbId = request.TvdbId,
                PosterUrl = request.PosterUrl,
                Status = RemovalStatus.Voting,
                Created = Clock()
            };
        }

        private async Task StartVoting(RemovalProposal proposal)
        {
            proposal.Status = RemovalStatus.Voting;
            proposal.VotingStarted = Clock();
            proposal.WindowClosesAt = Clock() + _settings.VoteWindow;
            Save(proposal);
            await SyncPrompts(proposal);
            await Changed(proposal);
        }

        // Every active user with access to the type gets the prompt, including anyone who gained
        // access since the vote opened. The one who asked for it and a requester who said yes don't.
        private async Task SyncPrompts(RemovalProposal proposal)
        {
            var prompted = _prompts.ForProposal(proposal.Id).Select(p => p.UserId).ToHashSet();
            var skip = new HashSet<int>();

            if (!proposal.InitiatedByAdmin && proposal.InitiatedByUserId.HasValue)
            {
                skip.Add(proposal.InitiatedByUserId.Value);
            }

            if (proposal.RequesterResponse == RequesterResponse.Yes && proposal.RequesterUserId.HasValue)
            {
                skip.Add(proposal.RequesterUserId.Value);
            }

            var voters = _users.All()
                .Where(u => u.Status == UserStatus.Active && u.PrivateChannelId != null && !prompted.Contains(u.Id) && !skip.Contains(u.Id))
                .Where(u => _access.GetForUser(u.Id).Any(a => a.ContentType == proposal.ContentType && a.State != AccessState.Off))
                .ToList();

            if (voters.Count == 0)
            {
                return;
            }

            var card = await Card(proposal);

            foreach (var voter in voters)
            {
                var message = await PostToUser(voter.Id, DiscordText.RemovalPrompt(proposal, proposal.WindowClosesAt.Value, false), KeepButtons(proposal, false), card);
                _prompts.Insert(new RemovalPrompt { ProposalId = proposal.Id, UserId = voter.Id, ChannelId = message.Channel, MessageId = message.Id, Sent = Clock() });
            }
        }

        private async Task CloseVote(RemovalProposal proposal)
        {
            var flags = _flags.ForProposal(proposal.Id);

            if (flags.Count == 0)
            {
                _audit.Record(Actor.System, "removal.vote-passed", "removal", proposal.Id.ToString(), $"{Scope(proposal)}: no keep flags");
                await DeleteNow(proposal, null, null, null);
                return;
            }

            // Any one flag keeps it. Users are told it's kept; the admin can still force it.
            proposal.Status = RemovalStatus.Flagged;
            proposal.Closed = Clock();
            Save(proposal);
            _audit.Record(Actor.System, "removal.kept", "removal", proposal.Id.ToString(), $"{Scope(proposal)}: {flags.Count} keep flag(s)");
            await PostResults(proposal);
            await Changed(proposal);
        }

        private async Task<ServiceResult> DeleteNow(RemovalProposal proposal, Actor admin, string adminName, int? adminUserId)
        {
            if (proposal.RecordOnly)
            {
                return await DropRecord(proposal, admin, adminName, adminUserId);
            }

            var instance = _backends.Find(proposal.BackendId ?? 0);

            try
            {
                if (instance == null)
                {
                    throw new BackendException(DiscordText.RemovalInstanceGone);
                }

                if (!_settings.DeleteFiles)
                {
                    return await WaitForDeleteByHand(proposal, instance, admin, adminName, adminUserId);
                }

                if (proposal.WholeTitle)
                {
                    await _backends.ApiFor(instance).DeleteItemAsync(instance.Config, proposal.ExternalItemId.Value);
                }
                else
                {
                    await ((ISonarrApi)_backends.ApiFor(instance)).DeleteSeasonsAsync(instance.Config, proposal.ExternalItemId.Value, proposal.Seasons);
                }
            }
            catch (BackendException ex)
            {
                _logger.Warn(ex, "Removing {0} from {1} failed", proposal.Label, instance?.Name);
                proposal.Status = RemovalStatus.Failed;
                proposal.Detail = ex.Message;
                proposal.Closed = Clock();
                Save(proposal);
                _audit.Record(admin ?? Actor.System, "removal.failed", "removal", proposal.Id.ToString(), ex.Message);
                await Changed(proposal);
                return ServiceResult.Fail(DiscordText.RemovalFailed(proposal, instance?.Name));
            }

            CloseAsRemoved(proposal, admin, adminName, adminUserId);
            _audit.Record(admin ?? Actor.System, "removal.deleted", "removal", proposal.Id.ToString(), $"{Scope(proposal)} deleted from {instance.Name} with its files, not excluded{How(admin, adminName)}");

            // The requests for it are closed (whole title) or lose those seasons; a later request is fresh.
            foreach (var request in _requests.ForTitle(proposal.ContentType, proposal.ExternalId ?? 0).Where(r => r.BackendId == proposal.BackendId && r.IsActive).ToList())
            {
                await _requestService.ApplyRemovalAsync(request, proposal.Seasons, DiscordText.RemovalNote(proposal));
            }

            await PostResults(proposal);
            await Changed(proposal);
            return ServiceResult.Ok(DiscordText.RemovalDone(proposal, instance.Name));
        }

        // Sonarr and Radarr don't have it, so nothing is deleted anywhere, whether or not deleting files is on (there's
        // nothing to delete by hand either). The removal closes as a passed one and everyone asked is told; then the request
        // leaves Requestarr's lists (its record is deleted; the audit log keeps it). An import never links a request while
        // its removal is open, so it is still Not in library here and DeleteRecord drops it.
        private async Task<ServiceResult> DropRecord(RemovalProposal proposal, Actor admin, string adminName, int? adminUserId)
        {
            CloseAsRemoved(proposal, admin, adminName, adminUserId);
            _audit.Record(admin ?? Actor.System, "removal.deleted", "removal", proposal.Id.ToString(), $"{Scope(proposal)} dropped from Requestarr's lists; it isn't in the library, so nothing was deleted{How(admin, adminName)}");

            // Its feed card and messages show the outcome first, while the request is still there to show.
            await PostResults(proposal);
            await Changed(proposal);

            var requestId = proposal.RequestId.Value;
            _requestService.DeleteRecord(requestId, admin ?? Actor.System);
            proposal.RequestId = null;
            Save(proposal);
            ForgetRequest(requestId);

            return ServiceResult.Ok(DiscordText.RemovalRecordDropped(proposal));
        }

        // Earlier removals of a dropped request (kept or called off) stop pointing at it too, so the audit log and the
        // web app show their title without a link to a page that's gone.
        private void ForgetRequest(int requestId)
        {
            foreach (var earlier in _proposals.ForRequest(requestId))
            {
                earlier.RequestId = null;
                Save(earlier);
            }
        }

        // Passed: by vote (no admin), or forced by the admin, who is named on it.
        private void CloseAsRemoved(RemovalProposal proposal, Actor admin, string adminName, int? adminUserId)
        {
            proposal.Status = admin != null ? RemovalStatus.Forced : RemovalStatus.Deleted;
            proposal.Closed = Clock();
            proposal.ClosedByUserId = adminUserId;
            proposal.ClosedByName = adminName;
            Save(proposal);
        }

        private static string How(Actor admin, string adminName) => admin != null ? $", forced by {adminName}" : ", by vote";

        // Deleting files is off on this server: everything a deletion does except deleting. The vote is closed, the
        // outcome recorded and everyone asked is told, and the admin queue gets it to delete by hand. The requests stay
        // until the admin says it's gone.
        private async Task<ServiceResult> WaitForDeleteByHand(RemovalProposal proposal, Backends.BackendInstance instance, Actor admin, string adminName, int? adminUserId)
        {
            proposal.Status = RemovalStatus.DeleteByHand;
            proposal.Closed = Clock();
            proposal.ClosedByUserId = adminUserId;
            proposal.ClosedByName = adminName;
            Save(proposal);
            var how = admin != null ? $"forced by {adminName}" : "by vote";
            _audit.Record(admin ?? Actor.System, "removal.delete-by-hand", "removal", proposal.Id.ToString(), $"{Scope(proposal)} agreed ({how}); deleting files is off on this server, so it waits for an admin to delete it from {instance.Name}");

            await PostResults(proposal);
            await Changed(proposal);
            return ServiceResult.Ok(DiscordText.RemovalDeleteByHand(proposal, instance.Name));
        }

        public bool DeletesFiles => _settings.DeleteFiles;

        public async Task<ServiceResult> DeletedByHandAsync(int proposalId, Actor admin, string adminName, int? adminUserId)
        {
            var proposal = _proposals.Find(proposalId);

            if (proposal?.Status != RemovalStatus.DeleteByHand)
            {
                return ServiceResult.Fail(proposal == null ? DiscordText.RemovalNotFound : DiscordText.RemovalAlreadyMoved(proposal));
            }

            // Who forced it, if anyone, stays on it; otherwise it was the vote.
            proposal.Status = proposal.ClosedByName != null ? RemovalStatus.Forced : RemovalStatus.Deleted;
            proposal.Closed = Clock();
            Save(proposal);
            var instance = _backends.Find(proposal.BackendId ?? 0);
            _audit.Record(admin, "removal.deleted-by-hand", "removal", proposal.Id.ToString(), $"{Scope(proposal)} deleted from {instance?.Name ?? "the app that had it"} by hand, said {adminName}");

            foreach (var request in _requests.ForTitle(proposal.ContentType, proposal.ExternalId ?? 0).Where(r => r.BackendId == proposal.BackendId && r.IsActive).ToList())
            {
                await _requestService.ApplyRemovalAsync(request, proposal.Seasons, DiscordText.RemovalNote(proposal));
            }

            await PostResults(proposal);
            await Changed(proposal);
            return ServiceResult.Ok(DiscordText.RemovalDeletedByHand(proposal));
        }

        // Everyone prompted, and the requester, get their message reposted with the outcome, no buttons.
        private async Task PostResults(RemovalProposal proposal)
        {
            var outcome = DiscordText.RemovalOutcome(proposal);
            var card = await Card(proposal);

            foreach (var prompt in _prompts.ForProposal(proposal.Id))
            {
                var id = await Repost(prompt.ChannelId, prompt.MessageId, prompt.UserId, outcome, null, card);
                prompt.MessageId = id ?? prompt.MessageId;
                _prompts.Update(prompt);
            }

            if (proposal.RequesterUserId.HasValue && proposal.RequesterMessageId != null && _prompts.ForProposal(proposal.Id).All(p => p.UserId != proposal.RequesterUserId))
            {
                // Deleted: their request message is reposted with the outcome, so this one just goes. A record-only
                // removal's request goes without a message, so they get the outcome here.
                var requestReposted = proposal.Status is RemovalStatus.Deleted or RemovalStatus.Forced && !proposal.RecordOnly && proposal.RequestId.HasValue &&
                                      _requests.Find(proposal.RequestId.Value)?.OwnerUserId == proposal.RequesterUserId;

                if (requestReposted && ulong.TryParse(proposal.RequesterChannelId, out var channel) && ulong.TryParse(proposal.RequesterMessageId, out var message))
                {
                    await _gateway.DeleteMessageAsync(channel, message);
                }
                else
                {
                    proposal.RequesterMessageId = await Repost(proposal.RequesterChannelId, proposal.RequesterMessageId, proposal.RequesterUserId.Value, outcome, null, card) ?? proposal.RequesterMessageId;
                    Save(proposal);
                }
            }
        }

        // The feed follows every step (the request's card, or the removal's own for ownerless titles),
        // and /admin requests may have gained or lost an item.
        private async Task Changed(RemovalProposal proposal)
        {
            await _feedOrCard(proposal);
            _events.PublishEvent(new AdminQueueChangedEvent());
        }

        private async Task _feedOrCard(RemovalProposal proposal)
        {
            var request = proposal.RequestId.HasValue ? _requests.Find(proposal.RequestId.Value) : null;

            if (request != null)
            {
                await _feed.PostAsync(request);

                // Once removed, the request message was just reposted with the outcome.
                if (proposal.Status is not (RemovalStatus.Deleted or RemovalStatus.Forced))
                {
                    await _requestService.RefreshOwnerMessageAsync(request);
                }
            }
            else
            {
                await _feed.PostRemovalAsync(proposal);
            }
        }

        private async Task EditRequesterMessage(RemovalProposal proposal, string content)
        {
            if (!ulong.TryParse(proposal.RequesterChannelId, out var channel) || !ulong.TryParse(proposal.RequesterMessageId, out var message))
            {
                return;
            }

            try
            {
                await _gateway.EditMessageAsync(channel, message, new InteractionReply { Content = $"{Mention(proposal.RequesterUserId.Value)} {content}", KeepEmbeds = true });
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Removal {0}: the requester's message could not be edited", proposal.Id);
            }
        }

        private async Task<(string Channel, string Id)> PostToUser(int userId, string content, List<InteractionButton> buttons = null, InteractionEmbed card = null)
        {
            var user = _users.Find(userId);

            if (user?.Status != UserStatus.Active || !ulong.TryParse(user.PrivateChannelId, out var channel))
            {
                return (null, null);
            }

            try
            {
                var id = await _gateway.SendMessageAsync(channel, Message(userId, content, buttons, card));
                return (channel.ToString(), id.ToString());
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "Could not post a removal message to user {0}", userId);
                return (null, null);
            }
        }

        // Deletes the old message and posts the new one at the bottom (the post is the notification).
        private async Task<string> Repost(string channelId, string messageId, int userId, string content, List<InteractionButton> buttons, InteractionEmbed card)
        {
            if (!ulong.TryParse(channelId, out var channel))
            {
                return null;
            }

            try
            {
                if (ulong.TryParse(messageId, out var old) && !await _gateway.DeleteMessageAsync(channel, old))
                {
                    await _gateway.EditMessageAsync(channel, old, new InteractionReply { Content = content, KeepEmbeds = true });
                }

                return (await _gateway.SendMessageAsync(channel, Message(userId, content, buttons, card))).ToString();
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Removal message for user {0} could not be reposted", userId);
                return null;
            }
        }

        private InteractionReply Message(int userId, string content, List<InteractionButton> buttons, InteractionEmbed card) => new()
        {
            Content = DiscordLimits.Truncate($"{Mention(userId)} {content}".Trim(), DiscordLimits.MessageLength),
            Embeds = card == null ? new List<InteractionEmbed>() : new List<InteractionEmbed> { card },
            Buttons = buttons ?? new List<InteractionButton>()
        };

        private async Task<InteractionEmbed> Card(RemovalProposal proposal)
        {
            var instance = _backends.Find(proposal.BackendId ?? 0);
            var item = instance == null || !proposal.ExternalId.HasValue ? null : await _requestService.LookupItemAsync(instance, proposal.ContentType, proposal.ExternalId.Value);

            return item == null
                ? new InteractionEmbed { Title = proposal.Label, ThumbnailUrl = proposal.PosterUrl, Color = RequestCards.CancelledColor }
                : RequestCards.Card(proposal.ContentType, item, RequestCards.CancelledColor, compact: true);
        }

        private DateTime? ReminderAt(RemovalProposal proposal) =>
            _settings.ReminderAfter is { } after ? proposal.Created + after : null;

        private DateTime Closes() => Clock() + _settings.VoteWindow;

        private static List<InteractionButton> AnswerButtons(RemovalProposal proposal) => new()
        {
            new() { CustomId = $"{RemovalRoutes.Answer}:{proposal.Id}:y", Label = DiscordText.RemovalYesLabel, Style = ButtonStyle.Danger },
            new() { CustomId = $"{RemovalRoutes.Answer}:{proposal.Id}:n", Label = DiscordText.RemovalNoLabel, Style = ButtonStyle.Success }
        };

        public static List<InteractionButton> KeepButtons(RemovalProposal proposal, bool flagged) => new()
        {
            flagged
                ? new() { CustomId = $"{RemovalRoutes.Unkeep}:{proposal.Id}", Label = DiscordText.RemovalTakeBackLabel, Style = ButtonStyle.Secondary }
                : new() { CustomId = $"{RemovalRoutes.Keep}:{proposal.Id}", Label = DiscordText.RemovalKeepLabel, Style = ButtonStyle.Success }
        };

        private string Mention(int userId)
        {
            var discordId = _identities.GetForUser(userId).FirstOrDefault(i => i.Provider == UserIdentity.DiscordProvider)?.ExternalId;
            return discordId == null ? string.Empty : $"<@{discordId}>";
        }

        private static string Scope(RemovalProposal proposal) => $"{proposal.Label}{(proposal.WholeTitle ? string.Empty : $" {DiscordText.PlainSeasons(proposal.Seasons)}")}";

        private void Save(RemovalProposal proposal) => _proposals.Update(proposal);
    }
}
