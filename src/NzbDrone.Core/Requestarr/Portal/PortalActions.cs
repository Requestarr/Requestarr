using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Requestarr.Core.Audit;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Removals;
using Requestarr.Core.Requests;
using Requestarr.Core.Users;

namespace Requestarr.Core.Portal
{
    // A title in a Sonarr or Radarr library with no active request here, that a removal can start on, from the header
    // search. Title: as Sonarr or Radarr has it, without the year (Label adds it), so the search finds it again.
    public record PortalLibraryTitle(int InstanceId, int ItemId, string Instance, string Title, string Label, IReadOnlyList<int> Seasons, string Poster);

    public interface IPortalActions
    {
        Task<ServiceResult> ApproveAsync(int requestId, Actor admin);
        Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin);
        Task<ServiceResult> CancelAsync(int requestId, string reason, Actor admin);
        Task<ServiceResult> MarkAvailableAsync(int requestId, Actor admin);
        Task<ServiceResult> SearchAgainAsync(int requestId, Actor admin);
        ServiceResult DeleteRecord(int requestId, Actor admin);
        Task<ServiceResult> RetryAsync(int requestId, Actor admin);
        Task<ServiceResult> AddSeasonsAsync(int requestId, SeasonChoice seasons, Actor admin);

        // Seasons empty: the whole title. Force deletes now instead of asking the requester or holding a vote.
        Task<ServiceResult> StartRemovalAsync(int requestId, IReadOnlyCollection<int> seasons, bool force, Actor admin);
        Task<ServiceResult> StartLibraryRemovalAsync(int instanceId, int itemId, IReadOnlyCollection<int> seasons, bool force, Actor admin);
        Task<IReadOnlyList<PortalLibraryTitle>> SearchUnrequestedAsync(string term);

        // Whether a removal deletes in Sonarr or Radarr here; when it doesn't, the web app's confirmations say so.
        bool DeletesFiles { get; }
        Task<ServiceResult> ForceRemovalAsync(int removalId, Actor admin);
        Task<ServiceResult> DropRemovalAsync(int removalId, Actor admin);
        Task<ServiceResult> DeletedByHandAsync(int removalId, Actor admin);

        Task<ServiceResult> CheckInstanceAsync(int instanceId);
        Task<ServiceResult> AnnounceOutageAsync(int instanceId, Actor admin);
    }

    // What an admin does to requests, removals and instances in the portal (docs/admin-portal.md, 5c). Each goes
    // through the same service as its Discord button, with the same rules, so Discord's messages change as if it had
    // been pressed there; Discord names the admin as "name (web app)".
    public class PortalActions : IPortalActions
    {
        private readonly IRequestService _requests;
        private readonly IRemovalService _removals;
        private readonly IBackendService _backends;
        private readonly IConnectionMonitor _monitor;

        public PortalActions(IRequestService requests, IRemovalService removals, IBackendService backends, IConnectionMonitor monitor)
        {
            _requests = requests;
            _removals = removals;
            _backends = backends;
            _monitor = monitor;
        }

        public Task<ServiceResult> ApproveAsync(int requestId, Actor admin) => _requests.ApproveAsync(requestId, admin, NameOf(admin));

        public Task<ServiceResult> DenyAsync(int requestId, string reason, Actor admin) => _requests.DenyAsync(requestId, reason, admin, NameOf(admin));

        public Task<ServiceResult> CancelAsync(int requestId, string reason, Actor admin) => _requests.AdminCancelAsync(requestId, reason, admin, NameOf(admin));

        public Task<ServiceResult> MarkAvailableAsync(int requestId, Actor admin) => _requests.MarkAvailableAsync(requestId, admin, NameOf(admin));

        public Task<ServiceResult> SearchAgainAsync(int requestId, Actor admin) => _requests.SearchAgainAsync(requestId, admin);

        public ServiceResult DeleteRecord(int requestId, Actor admin) => _requests.DeleteRecord(requestId, admin);

        public bool DeletesFiles => _removals.DeletesFiles;

        public Task<ServiceResult> RetryAsync(int requestId, Actor admin) => _requests.RetrySendAsync(requestId, admin);

        // Typed seasons, so each must be one the show has to add, as Discord's picker only offers those.
        public async Task<ServiceResult> AddSeasonsAsync(int requestId, SeasonChoice seasons, Actor admin)
        {
            if (_requests.Find(requestId) is not { } request)
            {
                return ServiceResult.Fail(DiscordText.RequestNotFound);
            }

            if (await _requests.ItemForAsync(request) is not { } item)
            {
                return ServiceResult.Fail(DiscordText.TitleNotFound);
            }

            var addable = RequestCards.AddableSeasons(request, item);
            var unknown = seasons.Seasons.Except(addable).ToList();

            if (unknown.Any())
            {
                return ServiceResult.Fail($"{request.Label} has no season {string.Join(", ", unknown)} to add. It can add: {(addable.Any() ? string.Join(", ", addable) : "none")}.");
            }

            return await _requests.AddSeasonsAsync(requestId, null, true, admin, seasons);
        }

        public async Task<ServiceResult> StartRemovalAsync(int requestId, IReadOnlyCollection<int> seasons, bool force, Actor admin)
        {
            var target = await _removals.TargetForRequestAsync(requestId);

            if (target.Error != null)
            {
                return new ServiceResult(false, target.Error);
            }

            return await StartLibraryRemovalAsync(target.Value.Instance.Id, target.Value.Title.Id, seasons, force, admin);
        }

        public Task<ServiceResult> StartLibraryRemovalAsync(int instanceId, int itemId, IReadOnlyCollection<int> seasons, bool force, Actor admin) =>
            _removals.StartByAdminAsync(admin, NameOf(admin), admin.UserId, instanceId, itemId, seasons ?? new List<int>(), force);

        public async Task<IReadOnlyList<PortalLibraryTitle>> SearchUnrequestedAsync(string term)
        {
            var found = await _removals.SearchLibraryAsync(term ?? string.Empty, unrequestedOnly: true);

            return found.Select(t => new PortalLibraryTitle(t.Instance.Id, t.Title.Id, t.Instance.Name, t.Title.Title, t.Label, t.Title.SeasonsWithFiles ?? new List<int>(), t.Title.PosterUrl))
                .ToList();
        }

        public Task<ServiceResult> ForceRemovalAsync(int removalId, Actor admin) => _removals.ForceAsync(removalId, admin, NameOf(admin), admin.UserId);

        public Task<ServiceResult> DropRemovalAsync(int removalId, Actor admin) => _removals.DropAsync(removalId, admin, NameOf(admin), admin.UserId);

        public Task<ServiceResult> DeletedByHandAsync(int removalId, Actor admin) => _removals.DeletedByHandAsync(removalId, admin, NameOf(admin), admin.UserId);

        // Check now, as the waiting instance's card has it in Discord.
        public async Task<ServiceResult> CheckInstanceAsync(int instanceId)
        {
            if (_backends.Find(instanceId) is not { } instance)
            {
                return new ServiceResult(false, "That instance no longer exists.");
            }

            var back = await _monitor.CheckAsync(instance);
            return new ServiceResult(back, back ? DiscordText.InstanceBackNow(instance.Name) : DiscordText.StillDown(instance.Name));
        }

        // Announce to users, as the down alert's button has it. The monitor answers in words for the admin; it posted
        // the notice when it names no problem.
        public async Task<ServiceResult> AnnounceOutageAsync(int instanceId, Actor admin)
        {
            var before = _backends.Find(instanceId)?.DownAnnounceMessageId;
            var answer = await _monitor.AnnounceAsync(instanceId, NameOf(admin));

            return new ServiceResult(before == null && _backends.Find(instanceId)?.DownAnnounceMessageId != null, answer);
        }

        // How Discord's cards name the admin: "owner (web app)".
        public static string NameOf(Actor admin) => PortalText.Actor(admin.Description);
    }
}
