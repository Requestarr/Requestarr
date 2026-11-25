using System;
using System.Linq;
using System.Threading.Tasks;
using NLog;
using Requestarr.Core.Admin;
using Requestarr.Core.Requests;

namespace Requestarr.Core.Imports
{
    public interface IImportSummary
    {
        // One post in the admin feed for what imports' first checks found in trouble, once for each request.
        Task PostAsync();
    }

    // What was already stuck or failed when it was imported raises no alerts; admins hear of it here, once. A request whose
    // Sonarr or Radarr hasn't answered yet waits for the check that reaches it; a post that fails is tried after the next.
    public class ImportSummary : IImportSummary
    {
        private readonly IRequestRepository _requests;
        private readonly IRequestFeed _feed;
        private readonly Logger _logger;

        public ImportSummary(IRequestRepository requests, IRequestFeed feed, Logger logger)
        {
            _requests = requests;
            _feed = feed;
            _logger = logger;
        }

        public async Task PostAsync()
        {
            var waiting = _requests.All().Where(r => r.SummaryDue && !r.FirstCheckDue).ToList();

            if (waiting.Count == 0)
            {
                return;
            }

            try
            {
                await _feed.PostImportSummaryAsync(waiting.Where(TroubleSource.InTrouble).ToList());

                waiting.ForEach(r => r.SummaryDue = false);
                _requests.SetFields(waiting, r => r.SummaryDue);
            }
            catch (Exception ex)
            {
                _logger.Warn(ex, "The import's summary couldn't be posted; it's tried again after the next check");
            }
        }
    }
}
