using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Discord.Gateway;
using Requestarr.Core.Discord.Interactions;
using Requestarr.Core.Users;

namespace Requestarr.Core.Requests
{
    // The admin feed channel: one compact card per request. Every change deletes the old card and
    // posts a fresh one at the bottom, so the newest activity is always last.
    public interface IRequestFeed
    {
        Task PostAsync(Request request, MediaLookupResult item = null);

        // An imported request's card, marked Imported with the day it was asked for, silent; its message ID.
        Task<ulong> PostImportedAsync(Request request);

        // What an import's first check found that needs attention: one silent card, nothing when there's nothing.
        Task PostImportSummaryAsync(IReadOnlyCollection<Request> needAttention);

        // A removal of a title with no request (never requested through Requestarr): its own card.
        Task PostRemovalAsync(Removals.RemovalProposal removal);

        // The card as it stands, for refreshing a card in place after a late click.
        InteractionReply CardFor(Request request);

        // Once per feed channel: cards for requests still waiting or in progress (not finished ones).
        Task SeedAsync();
    }

    public class RequestFeed : IRequestFeed
    {
        private readonly IDiscordGuildGateway _gateway;
        private readonly IDiscordGuildConfigRepository _configs;
        private readonly IRequestRepository _requests;
        private readonly IRequestSubscriberRepository _subscribers;
        private readonly IRequestarrUserRepository _users;
        private readonly IBackendService _backends;
        private readonly Removals.IRemovalProposalRepository _removals;
        private readonly Removals.IRemovalFlagRepository _flags;
        private readonly IPortalLinks _portal;
        private readonly Logger _logger;

        public RequestFeed(IDiscordGuildGateway gateway,
                           IDiscordGuildConfigRepository configs,
                           IRequestRepository requests,
                           IRequestSubscriberRepository subscribers,
                           IRequestarrUserRepository users,
                           IBackendService backends,
                           Removals.IRemovalProposalRepository removals,
                           Removals.IRemovalFlagRepository flags,
                           IPortalLinks portal,
                           Logger logger)
        {
            _removals = removals;
            _flags = flags;
            _gateway = gateway;
            _configs = configs;
            _requests = requests;
            _subscribers = subscribers;
            _users = users;
            _backends = backends;
            _portal = portal;
            _logger = logger;
        }

        public InteractionReply CardFor(Request request) => CardFor(request, false);

        public async Task<ulong> PostImportedAsync(Request request)
        {
            if (!ulong.TryParse(Config()?.FeedChannelId, out var channel))
            {
                return 0;
            }

            await ReplaceCardAsync(channel, request, CardFor(request, true));

            return ulong.Parse(request.FeedMessageId);
        }

        private InteractionReply CardFor(Request request, bool imported)
        {
            var followers = _subscribers.ForRequest(request.Id).Select(s => _users.Find(s.UserId)?.DisplayName ?? $"user {s.UserId}").ToList();
            var owner = _users.Find(request.OwnerUserId)?.DisplayName ?? $"user {request.OwnerUserId}";

            var all = _removals.ForRequest(request.Id);
            var removal = all.Where(p => p.IsOpen).OrderByDescending(p => p.Id).FirstOrDefault() ?? all.OrderByDescending(p => p.Id).FirstOrDefault();

            var instance = _backends.Find(request.BackendId ?? 0);
            var card = RequestCards.FeedCard(request, null, owner, instance?.Name, followers, removal, removal == null ? 0 : _flags.ForProposal(removal.Id).Count, imported);

            // The feed is the admins' channel: the title's page in Sonarr or Radarr, where a browser can reach it.
            if (TitleLinks.Open(instance, request) is { } open)
            {
                card.Buttons.Add(new InteractionButton { Label = DiscordText.OpenIn(instance.Implementation), Url = open });
            }

            return card;
        }

        public async Task PostImportSummaryAsync(IReadOnlyCollection<Request> needAttention)
        {
            if (needAttention.Count == 0 || !ulong.TryParse(Config()?.FeedChannelId, out var channel))
            {
                return;
            }

            var lines = needAttention
                .GroupBy(r => (Instance: _backends.Find(r.BackendId ?? 0)?.Name, Trouble: ImportTroubles.Of(r)))
                .Select(g => DiscordText.ImportTroubleLine(g.Key.Trouble, g.Key.Instance, g.Select(r => DiscordText.TitleLink(r.Label, _portal.Request(r.Id))).ToList()));

            await _gateway.SendMessageAsync(channel, RequestCards.ImportSummary(lines));
        }

        public async Task PostRemovalAsync(Removals.RemovalProposal removal)
        {
            if (!ulong.TryParse(Config()?.FeedChannelId, out var channel))
            {
                return;
            }

            try
            {
                var card = RequestCards.RemovalFeedCard(removal, _backends.Find(removal.BackendId ?? 0)?.Name, _flags.ForProposal(removal.Id).Count);

                if (ulong.TryParse(removal.FeedMessageId, out var old) && !await _gateway.DeleteMessageAsync(channel, old))
                {
                    await _gateway.EditMessageAsync(channel, old, new InteractionReply { KeepEmbeds = true });
                }

                removal.FeedMessageId = (await _gateway.SendMessageAsync(channel, card)).ToString();
                _removals.SetFields(removal, r => r.FeedMessageId);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Removal {0}: its feed card could not be posted", removal.Id);
            }
        }

        public async Task PostAsync(Request request, MediaLookupResult item = null)
        {
            if (!ulong.TryParse(Config()?.FeedChannelId, out var channel))
            {
                return;
            }

            try
            {
                var card = CardFor(request);

                if (request.PosterUrl == null && item?.PosterUrl != null)
                {
                    request.PosterUrl = item.PosterUrl;
                    card = CardFor(request);
                }

                await ReplaceCardAsync(channel, request, card);
                _requests.SetFields(request, r => r.PosterUrl);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Request {0}: its feed card could not be posted", request.Id);
            }
        }

        public async Task SeedAsync()
        {
            var config = Config();

            if (config == null || config.FeedSeeded || config.FeedChannelId == null)
            {
                return;
            }

            var open = _requests.All()
                .Where(r => r.Status == RequestStatus.Pending || r.Status == RequestStatus.Approved)
                .OrderBy(r => r.Requested).ThenBy(r => r.Id)
                .ToList();

            foreach (var request in open)
            {
                request.FeedMessageId = null;
                await PostAsync(request);
            }

            config.FeedSeeded = true;
            _configs.Upsert(config);
            _logger.Info("Admin feed seeded with {0} open request(s)", open.Count);
        }

        // One card per request: the old one goes (or, when it can't be deleted, loses its buttons) and the new one is
        // kept as the request's card.
        private async Task ReplaceCardAsync(ulong channel, Request request, InteractionReply card)
        {
            if (ulong.TryParse(request.FeedMessageId, out var old) && !await _gateway.DeleteMessageAsync(channel, old))
            {
                await _gateway.EditMessageAsync(channel, old, new InteractionReply { KeepEmbeds = true });
            }

            request.FeedMessageId = (await _gateway.SendMessageAsync(channel, card)).ToString();
            _requests.SetFields(request, r => r.FeedMessageId);
        }

        private DiscordGuildConfig Config() => _configs.FindByGuild(_gateway.GuildId.ToString());
    }
}
