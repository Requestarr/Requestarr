using System;
using NLog;
using NzbDrone.Core.Housekeeping;

namespace Requestarr.Core.Audit
{
    // Daily, with Servarr's own housekeeping: audit entries older than "Keep the audit log for" are deleted, and the trim
    // itself is recorded. Only the log is trimmed: requests, members and removals stay, and their pages show what's left.
    public class TrimAuditLog : IHousekeepingTask
    {
        public const string TrimAction = "audit.trim";

        private readonly IAuditRepository _repository;
        private readonly IAuditService _audit;
        private readonly IAuditSettings _settings;
        private readonly Logger _logger;

        public TrimAuditLog(IAuditRepository repository, IAuditService audit, IAuditSettings settings, Logger logger)
        {
            _repository = repository;
            _audit = audit;
            _settings = settings;
            _logger = logger;
        }

        public void Clean()
        {
            var keepFor = _settings.KeepFor;

            if (AuditRetention.Cutoff(keepFor, DateTime.UtcNow) is not { } cutoff)
            {
                return;
            }

            var trimmed = _repository.DeleteOlderThan(cutoff);

            if (trimmed == 0)
            {
                return;
            }

            var detail = $"Trimmed {trimmed} {(trimmed == 1 ? "entry" : "entries")} older than {keepFor}";
            _audit.Record(Actor.Automation, TrimAction, "audit", null, detail);
            _logger.Info("Audit log: {0}", detail);
        }
    }
}
